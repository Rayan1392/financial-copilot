FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/backend/FinancialCopilot.Worker/FinancialCopilot.Worker.csproj src/backend/FinancialCopilot.Worker/
COPY src/backend/FinancialCopilot.Application/FinancialCopilot.Application.csproj src/backend/FinancialCopilot.Application/
COPY src/backend/FinancialCopilot.Billing/FinancialCopilot.Billing.csproj src/backend/FinancialCopilot.Billing/
COPY src/backend/FinancialCopilot.Domain/FinancialCopilot.Domain.csproj src/backend/FinancialCopilot.Domain/
COPY src/backend/FinancialCopilot.Infrastructure/FinancialCopilot.Infrastructure.csproj src/backend/FinancialCopilot.Infrastructure/
RUN dotnet restore src/backend/FinancialCopilot.Worker/FinancialCopilot.Worker.csproj

COPY src/backend/ src/backend/
COPY docs/samim-font-v2.0.1/ docs/samim-font-v2.0.1/
RUN dotnet publish src/backend/FinancialCopilot.Worker/FinancialCopilot.Worker.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false
RUN rm -f /app/publish/appsettings.json /app/publish/appsettings.Development.json

FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine AS runtime
WORKDIR /app
RUN apk add --no-cache krb5-libs
COPY --from=build /app/publish ./
COPY docker/appsettings.Production.json ./appsettings.Production.json
USER app
ENTRYPOINT ["dotnet", "FinancialCopilot.Worker.dll"]
