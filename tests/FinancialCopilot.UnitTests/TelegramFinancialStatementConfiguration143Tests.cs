using FinancialCopilot.Infrastructure.Authentication;
using Microsoft.Extensions.Configuration;

namespace FinancialCopilot.UnitTests;

public sealed class TelegramFinancialStatementConfiguration143Tests
{
    [Fact]
    public void Environment_variable_binds_the_feature_option()
    {
        const string key = "ChannelFinancialStatements__AutoBackfillEnabled";
        var previous = Environment.GetEnvironmentVariable(key);
        try
        {
            Environment.SetEnvironmentVariable(key, "true");
            var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
            var options = new TelegramChannelFinancialStatementOptions();

            configuration.GetSection(TelegramChannelFinancialStatementOptions.SectionName).Bind(options);

            Assert.True(options.AutoBackfillEnabled);
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, previous);
        }
    }
}
