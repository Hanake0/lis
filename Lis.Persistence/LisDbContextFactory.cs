using Lis.Core.Util;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Lis.Persistence;

public class LisDbContextFactory :IDesignTimeDbContextFactory<LisDbContext> {
	public LisDbContext CreateDbContext(string[] args) {
		DotEnv.Load();

		DbContextOptionsBuilder<LisDbContext> optionsBuilder   = new();
		string?                               connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__lisdb")
														   ?? Environment.GetEnvironmentVariable("DATABASE_URL");

		if (string.IsNullOrEmpty(connectionString))
			throw new InvalidOperationException("ConnectionStrings__lisdb environment variable not set");

		optionsBuilder.UseNpgsql(connectionString, o => o.UseVector());

		return new LisDbContext(optionsBuilder.Options);
	}
}
