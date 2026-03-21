using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Phylet.Data.Configuration;

namespace Phylet.Data;

public sealed class DesignTimePhyletDbContextFactory : IDesignTimeDbContextFactory<PhyletDbContext>
{
    public PhyletDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<PhyletDbContext>();
        builder.UseSqlite("Data Source=phylet.design-time.db");

        return new PhyletDbContext(
            builder.Options,
            new RuntimeOptions(
                Manufacturer: "Phylet",
                ModelName: "Phylet Music Server",
                DefaultSubscriptionTimeoutSeconds: 1800));
    }
}
