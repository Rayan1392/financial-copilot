FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/backend/FinancialCopilot.TelegramGateway/FinancialCopilot.TelegramGateway.csproj src/backend/FinancialCopilot.TelegramGateway/
COPY src/backend/FinancialCopilot.Application/FinancialCopilot.Application.csproj src/backend/FinancialCopilot.Application/
COPY src/backend/FinancialCopilot.Domain/FinancialCopilot.Domain.csproj src/backend/FinancialCopilot.Domain/
RUN dotnet restore src/backend/FinancialCopilot.TelegramGateway/FinancialCopilot.TelegramGateway.csproj

COPY src/backend/ src/backend/
RUN dotnet publish src/backend/FinancialCopilot.TelegramGateway/FinancialCopilot.TelegramGateway.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false
RUN rm -f /app/publish/appsettings.Development.json

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
USER app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "FinancialCopilot.TelegramGateway.dll"]
