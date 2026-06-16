using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PaymentHub.Infrastructure.Postgres;

public class PaymentHubDbContextFactory : IDesignTimeDbContextFactory<PaymentHubDbContext>
{
    public PaymentHubDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("PAYMENTHUB_DESIGN_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=payment_gateway;Username=payment_gateway;Password=payment_gateway";

        var options = new DbContextOptionsBuilder<PaymentHubDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(PaymentHubDbContext).Assembly.GetName().Name))
            .Options;

        return new PaymentHubDbContext(options);
    }
}
