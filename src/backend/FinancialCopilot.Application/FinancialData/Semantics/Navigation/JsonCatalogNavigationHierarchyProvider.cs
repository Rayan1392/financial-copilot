using System.Text.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.Collections.Concurrent;

namespace FinancialCopilot.Application.FinancialData.Semantics.Navigation;

/// <summary>Reads the generated JSON hierarchy (a JSON subset artifact emitted from the canonical YAML catalog).</summary>
public sealed class JsonCatalogNavigationHierarchyProvider(string hierarchyPath, string catalogPath) :
    ICatalogNavigationHierarchyProvider, ICatalogNavigationOutlineProvider, ISchemaContextExpander
{
    private readonly Lazy<IReadOnlySet<string>> _monthlyMeasureIds = new(() => ReadMonthlyMeasureIdsFromDisk(catalogPath));
    private readonly ConcurrentDictionary<string, Lazy<MetricSemantic?>> _metricSemanticCache = new(StringComparer.Ordinal);

    private sealed record Node(string Id, string? ParentId,
        string[] Concepts, string[] Children, string[] Tables, string[] Metrics, string[] Dimensions, string[] Relationships,
        string[] DirectSourceItems, string[] ProviderIndicators, FinancialValueCatalogReference[] SourceItemReferences,
        string[] ProviderRestrictions, string[] UnitAndPeriodSemantics, bool NavigationApproved, bool SemanticApproved,
        bool Queryable, string? AuthorizationReason);

    public async Task<string> GetCompactOutlineAsync(string catalogVersion, string catalogHash, string question,
        int maximumCharacters, CancellationToken cancellationToken)
        => await GetCompactOutlineAsync(catalogVersion, catalogHash, question, [], maximumCharacters, cancellationToken);

    /// <summary>
    /// Loads the approved source-identity index. This is deliberately separate from the
    /// bounded/ranked Navigator outline and does not imply semantic approval or authorization.
    /// </summary>
    public async Task<IReadOnlyList<FinancialValueCatalogReference>> GetFinancialSourceReferencesAsync(
        string catalogVersion, string catalogHash, CancellationToken cancellationToken)
    {
        var document = await LoadAsync(catalogVersion, catalogHash, cancellationToken);
        using (document)
            return FinancialReferences(ParseNodes(document.RootElement).Values);
    }

    public async Task<string> GetCompactOutlineAsync(string catalogVersion, string catalogHash, string question,
        IReadOnlyList<string> requiredCatalogIds, int maximumCharacters, CancellationToken cancellationToken)
    {
        var document = await LoadAsync(catalogVersion, catalogHash, cancellationToken);
        using (document)
        {
            var nodes = ParseNodes(document.RootElement);
            var rootNodes = nodes.Values.Where(node => node.ParentId is null || !nodes.ContainsKey(node.ParentId)).ToArray();
            var rootIds = rootNodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
            var monthlyMeasureIds = _monthlyMeasureIds.Value;
            var metricSemantics = ReadMetricSemantics(nodes.Values.SelectMany(node => node.Metrics).Distinct(StringComparer.Ordinal),
                monthlyMeasureIds);
            var normalizedQuestion = Normalize(question);
            var questionTokens = Tokens(normalizedQuestion).ToHashSet(StringComparer.Ordinal);
            var scores = nodes.Values.ToDictionary(node => node.Id,
                node => MatchScore(normalizedQuestion, questionTokens, node, metricSemantics), StringComparer.Ordinal);
            var required = (requiredCatalogIds ?? []).ToHashSet(StringComparer.Ordinal);
            var requiredMonthlyMeasure = required.Any(id => id.StartsWith("metric_monthly_", StringComparison.Ordinal));
            var industryQuery = question.Contains("industry", StringComparison.OrdinalIgnoreCase)
                || question.Contains("\u0635\u0646\u0639\u062a", StringComparison.Ordinal);
            var monthlyDomainScore = nodes.Values
                .Where(node => node.Metrics.Any(id => id.StartsWith("metric_monthly_", StringComparison.Ordinal)))
                .Select(node => scores[node.Id]).DefaultIfEmpty(0).Max();
            var monthlyQuery = requiredMonthlyMeasure || monthlyDomainScore >= 20;

            var sourceCandidates = monthlyQuery ? [] : nodes.Values
                .Where(node => node.DirectSourceItems.Length > 0 && node.SourceItemReferences.Length > 0)
                .Select(node => (Node: node, Score: scores[node.Id]))
                .Where(candidate => candidate.Score >= 20)
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Node.Id, StringComparer.Ordinal)
                .Take(12).ToArray();

            var includedIds = new HashSet<string>(StringComparer.Ordinal);
            includedIds.UnionWith(rootIds);
            var requiredNodeIds = new List<string>();
            foreach (var catalogId in required.Order(StringComparer.Ordinal))
            {
                var bestMatch = nodes.Values.Where(node => HasCatalogReference(node, catalogId))
                    .OrderBy(ReferenceCount).ThenBy(node => node.Children.Length).ThenBy(node => node.Id, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (bestMatch is null) continue;
                AddAncestors(bestMatch);
                requiredNodeIds.Add(bestMatch.Id);
            }

            var sourceCandidateIds = sourceCandidates.Select(candidate => candidate.Node.Id).ToHashSet(StringComparer.Ordinal);
            var optionalNodes = nodes.Values
                .Where(node => !sourceCandidateIds.Contains(node.Id) && !rootIds.Contains(node.Id)
                    && !node.Id.StartsWith("source-items-", StringComparison.Ordinal)
                    && (node.Id != "domain-company-industry" || industryQuery)
                    && (!monthlyQuery || node.DirectSourceItems.Length == 0 && node.SourceItemReferences.Length == 0)
                    && scores[node.Id] >= 12)
                .OrderByDescending(node => required.Count == 0 ? 0 : RequiredReferenceScore(node, required))
                .ThenByDescending(node => scores[node.Id])
                .ThenBy(node => node.Id, StringComparer.Ordinal)
                .Take(10).ToArray();
            var activeOptionalNodeIds = optionalNodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var node in optionalNodes) AddAncestors(node);
            foreach (var candidate in sourceCandidates) AddAncestors(candidate.Node);

            var baseNodes = nodes.Values.Where(node => includedIds.Contains(node.Id))
                .OrderBy(node => node.Id, StringComparer.Ordinal).ToArray();
            var candidateByNode = sourceCandidates.ToDictionary(candidate => candidate.Node.Id,
                candidate => candidate, StringComparer.Ordinal);
            var selectedSourceItemsByNode = sourceCandidates
                .SelectMany(candidate => candidate.Node.SourceItemReferences
                    .Where(reference => candidate.Node.DirectSourceItems.Contains(reference.CatalogId, StringComparer.Ordinal))
                    .Select(reference => (candidate.Node.Id, Reference: reference,
                        Score: SourceItemScore(question, reference), NodeScore: candidate.Score)))
                .OrderByDescending(item => item.Score)
                .ThenByDescending(item => item.NodeScore)
                .ThenBy(item => item.Reference.CatalogId, StringComparer.Ordinal)
                .Take(12)
                .GroupBy(item => item.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key,
                    group => (IReadOnlySet<string>)group.Select(item => item.Reference.CatalogId).ToHashSet(StringComparer.Ordinal),
                    StringComparer.Ordinal);
            var serialized = baseNodes.Select(node => SerializeOutlineNode(node, question,
                candidateByNode.TryGetValue(node.Id, out var candidate) ? candidate : null, metricSemantics,
                includedIds, monthlyQuery, selectedSourceItemsByNode));
            var result = string.Join('\n', serialized);
            while (result.Length > maximumCharacters)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var removable = optionalNodes.Where(node => activeOptionalNodeIds.Contains(node.Id)
                        && !requiredNodeIds.Contains(node.Id, StringComparer.Ordinal))
                    .OrderBy(node => scores[node.Id]).ThenByDescending(node => node.Id, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (removable is null)
                {
                    var candidateToRemove = sourceCandidates.LastOrDefault();
                    if (candidateToRemove.Node is null) break;
                    sourceCandidates = sourceCandidates.Where(candidate => candidate.Node.Id != candidateToRemove.Node.Id).ToArray();
                    candidateByNode.Remove(candidateToRemove.Node.Id);
                }
                else
                {
                    activeOptionalNodeIds.Remove(removable.Id);
                }
                // Rebuild, rather than deleting a node and its direct children in place. A
                // pruned candidate/optional node may have added multiple ancestor levels;
                // leaving those orphan ancestors included defeats the budget reduction.
                includedIds.Clear();
                includedIds.UnionWith(rootIds);
                foreach (var nodeId in requiredNodeIds)
                    if (nodes.TryGetValue(nodeId, out var requiredNode)) AddAncestors(requiredNode);
                foreach (var nodeId in activeOptionalNodeIds)
                    if (nodes.TryGetValue(nodeId, out var optionalNode)) AddAncestors(optionalNode);
                foreach (var candidate in sourceCandidates) AddAncestors(candidate.Node);
                baseNodes = nodes.Values.Where(node => includedIds.Contains(node.Id))
                    .OrderBy(node => node.Id, StringComparer.Ordinal).ToArray();
                result = string.Join('\n', baseNodes.Select(node => SerializeOutlineNode(node, question,
                    candidateByNode.TryGetValue(node.Id, out var candidate) ? candidate : null, metricSemantics,
                    includedIds, monthlyQuery, selectedSourceItemsByNode)));
            }

            if (result.Length > maximumCharacters)
                throw new SchemaNavigationBudgetException("catalog_outline_budget_exceeded", result.Length, maximumCharacters);
            return result;

            void AddAncestors(Node node)
            {
                var current = node;
                while (true)
                {
                    includedIds.Add(current.Id);
                    if (current.ParentId is null || !nodes.TryGetValue(current.ParentId, out var parent)) break;
                    current = parent;
                }
            }
        }
    }

    private static string SerializeOutlineNode(Node node, string question, (Node Node, double Score)? candidate,
        IReadOnlyDictionary<string, MetricSemantic> metricSemantics, IReadOnlySet<string> includedNodeIds,
        bool monthlyQuery, IReadOnlyDictionary<string, IReadOnlySet<string>> selectedSourceItemsByNode)
    {
        var rankedConcepts = node.Concepts.Select(concept => (Concept: concept, Score: ConceptScore(question, concept)))
            .Where(item => item.Score > 0).OrderByDescending(item => item.Score).ThenBy(item => item.Concept, StringComparer.Ordinal)
            .Take(4).Select(item => item.Concept).ToArray();
        if (rankedConcepts.Length == 0 && node.ParentId == "financial-semantic-catalog")
            rankedConcepts = node.Concepts.Take(2).ToArray();
        var sourceItems = candidate is null ? [] : candidate.Value.Node.SourceItemReferences
            .Where(reference => selectedSourceItemsByNode.TryGetValue(node.Id, out var selectedIds)
                && selectedIds.Contains(reference.CatalogId))
            .Select(reference => new OutlineSourceItem(reference.CatalogId, reference.ProviderName, reference.StatementType,
                reference.SourceItemId, reference.TitleFa?.Trim(), reference.TitleEn?.Trim(), reference.MetricCode,
                node.Id, node.ParentId, candidate.Value.Score))
            .ToArray();
        var isSourceCandidate = candidate is not null;
        var row = new OutlineNode(node.Id, isSourceCandidate ? [] : rankedConcepts,
            isSourceCandidate ? [] : node.Children.Where(includedNodeIds.Contains).ToArray(),
            isSourceCandidate ? [] : node.Tables,
            isSourceCandidate ? [] : monthlyQuery
                ? node.Metrics.Where(id => id.StartsWith("metric_monthly_", StringComparison.Ordinal)).ToArray()
                : node.Metrics,
            isSourceCandidate ? [] : node.Metrics.Where(metricSemantics.ContainsKey)
                .Where(id => !monthlyQuery || id.StartsWith("metric_monthly_", StringComparison.Ordinal))
                .Select(id => metricSemantics[id]).Select(metric => new OutlineMetric(metric.CatalogId,
                    metric.ReferenceType, Compact(metric.Description, metric.CatalogId == "metric_monthly_sales" ? 240 : 120), metric.UnitCode, metric.UnitLabel,
                    metric.Aliases.Take(1).Select(alias => Compact(alias, 56)).ToArray())).ToArray(),
            isSourceCandidate ? [] : node.Dimensions,
            isSourceCandidate ? [] : node.Relationships,
            // Source item IDs are exposed through the bounded, ranked candidate objects above.
            // Emitting a node's full direct-source list here bypasses candidate ranking and can
            // add thousands of IDs to an otherwise compact outline.
            isSourceCandidate ? sourceItems.Select(item => item.CatalogId).ToArray() : [],
            isSourceCandidate ? [] : node.ProviderIndicators,
            node.NavigationApproved, node.SemanticApproved, node.Queryable, sourceItems);
        return JsonSerializer.Serialize(row, OutlineJsonOptions);

        static string Compact(string value, int maximumLength)
        {
            var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return normalized.Length <= maximumLength ? normalized : normalized[..(maximumLength - 1)] + "…";
        }
    }

    private static readonly JsonSerializerOptions OutlineJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private sealed record OutlineNode(string NodeId, string[] Concepts, string[] ChildNodeIds,
        string[] TableIds, string[] MetricIds, OutlineMetric[] MetricSemantics, string[] DimensionIds, string[] RelationshipIds, string[] DirectSourceItemIds,
        string[] ProviderFinancialIndicatorIds, bool NavigationApproved, bool SemanticApproved, bool Queryable,
        OutlineSourceItem[] SourceItemCandidates);

    private sealed record OutlineMetric(string CatalogId, string ReferenceType, string Description, string UnitCode,
        string UnitLabel, string[] Aliases);

    private sealed record MetricSemantic(string CatalogId, string ReferenceType, string Description, string UnitCode,
        string UnitLabel, string[] Aliases);

    private sealed record OutlineSourceItem(string CatalogId, string ProviderName, string StatementType,
        int SourceItemId, string? TitleFa, string? TitleEn, string? MetricCode, string NodeId, string? ParentNodeId, double MatchScore);

    public async Task<SchemaContext?> GetContextAsync(SchemaContextExpansionRequest request, CancellationToken cancellationToken)
    {
        if (request.ContractVersion != 1 || request.Depth < 0 || request.Depth > 4) return null;
        var document = await LoadAsync(request.CatalogVersion, request.CatalogHash, cancellationToken);
        using (document)
        {
            var all = ParseNodes(document.RootElement);
            var selected = new HashSet<string>(StringComparer.Ordinal);
            var expansionDiagnostics = new List<string>();
            var required = (request.RequiredCatalogIds ?? []).ToHashSet(StringComparer.Ordinal);
            var requiredNodes = new List<string>();
            foreach (var catalogId in required.Order(StringComparer.Ordinal))
            {
                var bestMatch = all.Values.Where(node => HasCatalogReference(node, catalogId))
                    .OrderBy(ReferenceCount).ThenBy(node => node.Children.Length).ThenBy(node => node.Id, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (bestMatch is null) continue;
                var current = bestMatch;
                while (true)
                {
                    requiredNodes.Add(current.Id);
                    if (current.ParentId is null || !all.TryGetValue(current.ParentId, out var parent)) break;
                    current = parent;
                }
            }
            var requiredFrontier = requiredNodes.Distinct(StringComparer.Ordinal).ToArray();
            var requestedFrontier = requiredFrontier
                .Concat(request.NodeIds.Distinct(StringComparer.Ordinal)).Distinct(StringComparer.Ordinal).ToArray();
            if (requestedFrontier.Length > 48)
                expansionDiagnostics.Add("context_expansion_node_budget_limited:initial_selection");
            var frontier = requestedFrontier.Take(48).ToArray();
            for (var depth = 0; depth <= request.Depth && frontier.Length > 0; depth++)
            {
                var next = new List<string>();
                foreach (var id in frontier)
                {
                    if (!all.TryGetValue(id, out var node) || !selected.Add(id)) continue;
                    if (depth < request.Depth)
                    {
                        var children = node.Children.Where(all.ContainsKey).Select(childId => all[childId])
                            .Select(child => (Node: child, Score: RequiredReferenceScore(child, required)))
                            .OrderByDescending(item => item.Score)
                            .ThenBy(item => item.Node.Id, StringComparer.Ordinal).ToArray();
                        if (children.Length > request.MaximumFanOut)
                        {
                            expansionDiagnostics.Add($"context_expansion_fanout_limited:{node.Id}");
                        }
                        // Expansion is a retrieval aid, not a license to walk unrelated branches.
                        // Independent catalog references define relevance; with no such references,
                        // keep the context at the explicitly selected nodes.
                        var candidates = required.Count == 0
                            ? []
                            : children.Where(item => item.Score > 0).ToArray();
                        var remainingNodeBudget = Math.Max(0, 48 - selected.Count - next.Distinct(StringComparer.Ordinal).Count());
                        var boundedChildren = candidates.Take(Math.Min(Math.Max(0, request.MaximumFanOut), remainingNodeBudget)).ToArray();
                        if (boundedChildren.Length < candidates.Length)
                            expansionDiagnostics.Add($"context_expansion_node_budget_limited:{node.Id}");
                        next.AddRange(boundedChildren.Select(item => item.Node.Id));
                    }
                }
                frontier = next.Distinct(StringComparer.Ordinal).ToArray();
            }

            var nodes = selected.Select(id => all[id]).ToArray();
            var governance = nodes.Select(node => new SchemaNodeGovernance(node.Id, true, node.NavigationApproved,
                node.SemanticApproved, node.Queryable)).ToArray();
            var notApproved = nodes.Where(node => !node.NavigationApproved).Select(node => $"navigation_metadata_pending:{node.Id}").ToArray();
            var notSemantic = nodes.Where(node => !node.SemanticApproved).Select(node => $"semantic_meaning_unapproved:{node.Id}").ToArray();
            var notQueryable = nodes.Where(node => !node.Queryable).Select(node => $"query_not_authorized:{node.Id}").ToArray();
            return new SchemaContext(1, request.CatalogVersion, request.CatalogHash,
                nodes.Select(node => node.Id).Order(StringComparer.Ordinal).ToArray(), governance,
                Flatten(nodes, node => node.Metrics), Flatten(nodes, node => node.Dimensions),
                EntityKinds(nodes),
                Flatten(nodes, node => node.Relationships), [], [], [], [],
                notApproved.Concat(notSemantic).Concat(notQueryable).Concat(expansionDiagnostics).ToArray(),
                FinancialValueReferences: FinancialReferences(nodes),
                ProviderFinancialIndicatorIds: Flatten(nodes, node => node.ProviderIndicators),
                MeasureRelationshipIds: ReadMonthlyMeasureRelationships(),
                MeasureGovernance: ReadMonthlyMeasureGovernance(),
                SourceSemantics: Flatten(nodes, node => node.ProviderRestrictions),
                UnitAndPeriodSemantics: Flatten(nodes, node => node.UnitAndPeriodSemantics));
        }
    }

    public async Task<SchemaContext?> ExpandAsync(SchemaContext context, IReadOnlyList<string> knownNodeIds, int depth,
        CancellationToken cancellationToken) => await GetContextAsync(new SchemaContextExpansionRequest(
            context.ContractVersion, context.CatalogVersion, context.CatalogHash, knownNodeIds, depth), cancellationToken);

    public async Task<SchemaContext?> ExpandAsync(SchemaContext context, IReadOnlyList<string> knownNodeIds, int depth,
        IReadOnlyList<string> requiredCatalogIds, CancellationToken cancellationToken) =>
        await GetContextAsync(new SchemaContextExpansionRequest(context.ContractVersion, context.CatalogVersion,
            context.CatalogHash, knownNodeIds, depth, RequiredCatalogIds: requiredCatalogIds), cancellationToken);

    private async Task<JsonDocument> LoadAsync(string expectedVersion, string expectedHash, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(hierarchyPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        try
        {
            var root = document.RootElement;
            CanonicalSemanticCatalogIntegrityValidator.Validate(catalogPath, root);
            var version = root.TryGetProperty("catalogVersion", out var versionNode) ? versionNode.GetString() : null;
            var hash = root.TryGetProperty("catalogHash", out var hashNode) ? hashNode.GetString() : null;
            if (!string.Equals(version, expectedVersion, StringComparison.Ordinal) ||
                !string.Equals(hash, expectedHash, StringComparison.Ordinal))
                throw new SchemaNavigationOutputException("catalog_hierarchy_identity_mismatch");
            return document;
        }
        catch { document.Dispose(); throw; }
    }

    private static Dictionary<string, Node> ParseNodes(JsonElement root)
    {
        var result = new Dictionary<string, Node>(StringComparer.Ordinal);
        foreach (var item in root.GetProperty("nodes").EnumerateArray())
        {
            var references = item.GetProperty("catalogReferences");
            var navigation = item.GetProperty("navigationMetadataApproval");
            var semantic = item.GetProperty("semanticApproval");
            var auth = item.GetProperty("queryAuthorization");
            var node = new Node(item.GetProperty("nodeId").GetString()!,
                item.GetProperty("parentNodeId").ValueKind == JsonValueKind.Null ? null : item.GetProperty("parentNodeId").GetString(),
                GetArray(item, "conceptsAndSynonyms"), GetArray(item, "childNodeIds"), GetArray(references, "tableIds"),
                GetArray(references, "metricIds"), GetArray(references, "dimensionIds"), GetArray(references, "relationshipIds"),
                GetArray(references, "directSourceItemIds"), GetArray(references, "providerFinancialIndicatorIds"),
                ParseFinancialReferences(item),
                GetArray(item, "providerRestrictions"), GetArray(item, "unitAndPeriodSemantics"),
                navigation.TryGetProperty("approved", out var navigationApproved) && navigationApproved.GetBoolean(),
                semantic.TryGetProperty("approved", out var semanticApproved) && semanticApproved.GetBoolean(),
                auth.TryGetProperty("queryable", out var queryable) && queryable.GetBoolean(),
                auth.TryGetProperty("reason", out var reason) ? reason.GetString() : null);
            if (!result.TryAdd(node.Id, node)) throw new SchemaNavigationOutputException("catalog_has_duplicate_node_ids");
        }
        foreach (var node in result.Values)
            if (node.Children.Any(child => !result.ContainsKey(child)))
                throw new SchemaNavigationOutputException("catalog_has_unknown_child_reference");
        return result;
    }

    private static string[] GetArray(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).ToArray()
            : [];

    private static string[] Flatten(IEnumerable<Node> nodes, Func<Node, IEnumerable<string>> selector) =>
        nodes.SelectMany(selector).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    /// <summary>Catalog review state per monthly measure; the context carries it so completeness can gate on it.</summary>
    private IReadOnlyDictionary<string, MeasureGovernance> ReadMonthlyMeasureGovernance()
    {
        var path = Path.Combine(catalogPath, "dictionaries", "monthly-measures.yaml");
        var result = new Dictionary<string, MeasureGovernance>(StringComparer.Ordinal);
        if (!File.Exists(path)) return result;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
            return result;
        foreach (var item in entries.EnumerateArray())
        {
            if (!item.TryGetProperty("catalogId", out var id) || id.ValueKind != JsonValueKind.String) continue;
            var review = item.TryGetProperty("review", out var reviewElement) && reviewElement.ValueKind == JsonValueKind.Object
                ? reviewElement : default;
            bool Flag(string name) => review.ValueKind == JsonValueKind.Object && review.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.True;
            var hasReview = review.ValueKind == JsonValueKind.Object;
            result[id.GetString()!] = new MeasureGovernance(Flag("semanticApproved"), Flag("queryable"),
                hasReview ? NullableString(review, "status") : null, hasReview ? NullableString(review, "provenance") : null);
        }
        return result;
    }

    private IReadOnlyDictionary<string, IReadOnlyList<string>> ReadMonthlyMeasureRelationships()
    {
        var path = Path.Combine(catalogPath, "dictionaries", "monthly-measures.yaml");
        if (!File.Exists(path)) return new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
            return new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        return entries.EnumerateArray().Where(item => item.TryGetProperty("catalogId", out _))
            .ToDictionary(item => item.GetProperty("catalogId").GetString()!,
                item => (IReadOnlyList<string>)GetArray(item, "relationshipIds"), StringComparer.Ordinal);
    }

    private static FinancialValueCatalogReference[] FinancialReferences(IEnumerable<Node> nodes) =>
        nodes.SelectMany(node => node.SourceItemReferences).DistinctBy(item => item.CatalogId, StringComparer.Ordinal)
            .OrderBy(item => item.CatalogId, StringComparer.Ordinal).ToArray();

    private static int RequiredReferenceScore(Node node, IReadOnlySet<string> required) =>
        // Shared dimensions such as dim_company belong in every domain. Using them as
        // branch-routing signals makes every domain appear relevant and inflates context.
        node.Metrics.Count(required.Contains)
        + node.Relationships.Count(required.Contains) + node.DirectSourceItems.Count(required.Contains)
        + node.ProviderIndicators.Count(required.Contains)
        + node.SourceItemReferences.Count(reference => required.Contains(reference.CatalogId));

    private static int ReferenceCount(Node node) => node.Metrics.Length + node.Dimensions.Length
        + node.Relationships.Length + node.DirectSourceItems.Length + node.ProviderIndicators.Length
        + node.SourceItemReferences.Length;

    private static bool HasCatalogReference(Node node, string catalogId) =>
        node.Metrics.Contains(catalogId, StringComparer.Ordinal)
        || node.Dimensions.Contains(catalogId, StringComparer.Ordinal)
        || node.Relationships.Contains(catalogId, StringComparer.Ordinal)
        || node.DirectSourceItems.Contains(catalogId, StringComparer.Ordinal)
        || node.ProviderIndicators.Contains(catalogId, StringComparer.Ordinal)
        || node.SourceItemReferences.Any(reference => string.Equals(reference.CatalogId, catalogId, StringComparison.Ordinal));

    private static FinancialValueCatalogReference[] ParseFinancialReferences(JsonElement item)
    {
        if (!item.TryGetProperty("sourceItemReferences", out var refs) || refs.ValueKind != JsonValueKind.Array) return [];
        return refs.EnumerateArray().Select(reference => new FinancialValueCatalogReference(
            String(reference, "catalogId"),
            Guid.TryParse(String(reference, "sourceItemCatalogId"), out var id) ? id : null,
            String(reference, "providerName"), String(reference, "statementType"),
            Int32(reference, "sourceItemId"), NullableString(reference, "titleFa"), NullableString(reference, "titleEn"),
            NullableString(reference, "sourceUnit"), NullableString(reference, "normalizedUnit"),
            NullableString(reference, "mappedMetricCode"), String(reference, "identityStatus"),
            String(reference, "semanticReviewStatus"),
            reference.TryGetProperty("queryAuthorization", out var auth)
                && auth.TryGetProperty("queryable", out var queryable) && queryable.ValueKind == JsonValueKind.True,
            NullableString(reference, "sourceUnitStatus"),
            ApprovalStatus(reference, "identityApproval"), ApprovalStatus(reference, "titleApproval"),
            ParseCanonicalConceptBindings(reference), NullableString(reference, "effectiveUnitStatus"),
            NullableString(reference, "unitClassification"), NullableString(reference, "effectiveUnit"),
            NullableString(reference, "effectiveUnitProvenance"),
            reference.TryGetProperty("queryAuthorization", out var queryAuthorization)
                ? NullableString(queryAuthorization, "state") : null)).ToArray();
    }

    private static string? ApprovalStatus(JsonElement reference, string propertyName) =>
        reference.TryGetProperty(propertyName, out var approval) && approval.ValueKind == JsonValueKind.Object
            ? NullableString(approval, "status") : null;

    private static IReadOnlyList<CanonicalFinancialMeasureBinding> ParseCanonicalConceptBindings(JsonElement reference)
    {
        if (!reference.TryGetProperty("canonicalConceptBindings", out var bindings)
            || bindings.ValueKind != JsonValueKind.Array) return [];
        return bindings.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => new CanonicalFinancialMeasureBinding(String(item, "canonicalConceptId"),
                String(item, "dimension"), String(item, "valueForm"), String(item, "status"),
                NullableString(item, "evidence"), NullableString(item, "provenance")))
            .Where(item => !string.IsNullOrWhiteSpace(item.CanonicalConceptId)).ToArray();
    }

    private static IReadOnlySet<string> ReadMonthlyMeasureIdsFromDisk(string catalogPath)
    {
        var path = Path.Combine(catalogPath, "dictionaries", "monthly-measures.yaml");
        if (!File.Exists(path)) return new HashSet<string>(StringComparer.Ordinal);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
            return new HashSet<string>(StringComparer.Ordinal);
        return entries.EnumerateArray().Select(item => String(item, "catalogId"))
            .Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.Ordinal);
    }

    private IReadOnlyDictionary<string, MetricSemantic> ReadMetricSemantics(IEnumerable<string> metricIds,
        IReadOnlySet<string> monthlyMeasureIds)
    {
        var result = new Dictionary<string, MetricSemantic>(StringComparer.Ordinal);
        foreach (var id in metricIds)
        {
            var semantic = _metricSemanticCache.GetOrAdd(id, key => new Lazy<MetricSemantic?>(
                () => ReadMetricSemanticFromDisk(catalogPath, key, monthlyMeasureIds.Contains(key),
                    nameof(FinancialValueReferenceType.MonthlyMeasure), nameof(FinancialValueReferenceType.GovernedMetric)),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
            if (semantic is not null) result[id] = semantic;
        }
        return result;
    }

    private static MetricSemantic? ReadMetricSemanticFromDisk(string catalogPath, string id, bool monthly,
        string monthlyReferenceType, string governedReferenceType)
    {
        var path = Path.Combine(catalogPath, "metrics", $"{id}.yaml");
        if (!File.Exists(path)) return null;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("sourceDefinition", out var definition)) return null;
        var aliases = definition.TryGetProperty("Aliases", out var aliasArray) && aliasArray.ValueKind == JsonValueKind.Array
            ? aliasArray.EnumerateArray().Where(alias => alias.TryGetProperty("Expression", out _))
                .Select(alias => String(alias, "Expression")).Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal).ToArray()
            : [];
        var unit = definition.TryGetProperty("Unit", out var unitNode) && unitNode.ValueKind == JsonValueKind.Object
            ? unitNode : default;
        return new MetricSemantic(id, monthly ? monthlyReferenceType : governedReferenceType,
            String(definition, "Description"),
            unit.ValueKind == JsonValueKind.Object ? String(unit, "Code") : string.Empty,
            unit.ValueKind == JsonValueKind.Object ? String(unit, "DisplayLabel") : string.Empty, aliases);
    }

    private static double MatchScore(string normalizedQuestion, IReadOnlySet<string> questionTokens, Node node,
        IReadOnlyDictionary<string, MetricSemantic> metricSemantics)
    {
        var best = 0d;
        foreach (var reference in node.SourceItemReferences)
        {
            foreach (var text in new[] { reference.TitleFa, reference.TitleEn, reference.MetricCode }.OfType<string>())
                best = Math.Max(best, ConceptScoreNormalized(normalizedQuestion, questionTokens, text));
        }
        foreach (var concept in node.Concepts)
            best = Math.Max(best, ConceptScoreNormalized(normalizedQuestion, questionTokens, concept));
        foreach (var metricId in node.Metrics)
        {
            if (!metricSemantics.TryGetValue(metricId, out var semantic)) continue;
            foreach (var alias in semantic.Aliases)
                best = Math.Max(best, ConceptScoreNormalized(normalizedQuestion, questionTokens, alias));
            best = Math.Max(best, ConceptScoreNormalized(normalizedQuestion, questionTokens, semantic.Description));
        }
        return best;
    }

    private static double SourceItemScore(string question, FinancialValueCatalogReference reference) =>
        new[] { reference.TitleFa, reference.TitleEn, reference.MetricCode }.OfType<string>()
            .Select(text => ConceptScore(question, text)).DefaultIfEmpty(0).Max();

    private static double ConceptScore(string question, string concept)
    {
        var normalizedQuestion = Normalize(question);
        return ConceptScoreNormalized(normalizedQuestion,
            Tokens(normalizedQuestion).ToHashSet(StringComparer.Ordinal), concept);
    }

    private static double ConceptScoreNormalized(string normalizedQuestion, IReadOnlySet<string> questionTokens,
        string concept)
    {
        var normalized = Normalize(concept);
        if (normalized.Length == 0) return 0;
        if (normalizedQuestion.Contains(normalized, StringComparison.Ordinal) && Tokens(normalized).Length >= 2)
            return 1000 + normalized.Length;
        var conceptTokens = Tokens(normalized).ToHashSet(StringComparer.Ordinal);
        if (conceptTokens.Count < 2) return 0;
        var overlap = conceptTokens.Count(questionTokens.Contains);
        if (overlap < 2) return 0;
        var queryCoverage = questionTokens.Count == 0 ? 0 : (double)overlap / questionTokens.Count;
        var conceptCoverage = (double)overlap / conceptTokens.Count;
        return overlap == 0 ? 0 : overlap * 10 + queryCoverage * 30 + conceptCoverage * 10;
    }

    private static string[] Tokens(string value) => value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static string Normalize(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormKC).ToLowerInvariant()
            .Replace('\u064A', '\u06CC').Replace('\u0643', '\u06A9').Replace('\u200c', ' ')
            .Replace('\u0640', ' ');
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
            builder.Append(char.IsLetterOrDigit(character) ? character : ' ');
        return string.Join(' ', builder.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string String(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static string? NullableString(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static int Int32(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : -1;

    private static string[] EntityKinds(IEnumerable<Node> nodes)
    {
        var dimensions = new HashSet<string>(nodes.SelectMany(node => node.Dimensions), StringComparer.Ordinal);
        var kinds = new List<string>();
        if (dimensions.Contains("dim_company")) kinds.Add("company");
        if (dimensions.Contains("dim_product_identity")) kinds.Add("product");
        if (dimensions.Contains("dim_industry")) kinds.Add("industry");
        if (dimensions.Contains("dim_market")) kinds.Add("market");
        if (dimensions.Contains("dim_trading_instrument")) kinds.Add("instrument");
        return kinds.ToArray();
    }
}
