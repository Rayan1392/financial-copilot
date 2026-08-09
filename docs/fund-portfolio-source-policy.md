# Fund portfolio source policy

Fund portfolio acquisition is provider-neutral. A source adapter must provide stable object
identifiers, bounded discovery, and a controlled download reference before it can be enabled.

No Codal URL, vendor endpoint, SEO page, or scraping behavior may be implemented from assumption.
An adapter requires verified source details, credentials/configuration policy, and sanitized fixtures.
Until then, manual upload and an explicitly configured local/object-storage prefix are the only
supported acquisition paths; scheduled discovery must remain unavailable when no verified adapter
is configured.
