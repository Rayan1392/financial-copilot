FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/backend/FinancialCopilot.API/FinancialCopilot.API.csproj src/backend/FinancialCopilot.API/
COPY src/backend/FinancialCopilot.Application/FinancialCopilot.Application.csproj src/backend/FinancialCopilot.Application/
COPY src/backend/FinancialCopilot.Billing/FinancialCopilot.Billing.csproj src/backend/FinancialCopilot.Billing/
COPY src/backend/FinancialCopilot.Domain/FinancialCopilot.Domain.csproj src/backend/FinancialCopilot.Domain/
COPY src/backend/FinancialCopilot.Infrastructure/FinancialCopilot.Infrastructure.csproj src/backend/FinancialCopilot.Infrastructure/
RUN dotnet restore src/backend/FinancialCopilot.API/FinancialCopilot.API.csproj

COPY src/backend/ src/backend/
COPY docs/samim-font-v2.0.1/ docs/samim-font-v2.0.1/
RUN dotnet publish src/backend/FinancialCopilot.API/FinancialCopilot.API.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false
RUN rm -f /app/publish/appsettings.json /app/publish/appsettings.Development.json

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
COPY docker/appsettings.Production.json ./appsettings.Production.json
USER app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "FinancialCopilot.API.dll"]
