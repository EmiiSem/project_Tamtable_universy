using Microsoft.EntityFrameworkCore;
using RukScheduleApp.Models;

namespace RukScheduleApp.Data
{
    public class ScheduleDbContext : DbContext
    {
        private readonly string _dbPath;

        public ScheduleDbContext(string dbPath)
        {
            _dbPath = dbPath;
            Database.EnsureCreated();
        }

        public DbSet<ScheduleItem> ScheduleItems { get; set; }
        public DbSet<Group> Groups { get; set; }
        public DbSet<Branch> Branches { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite($"Data Source={_dbPath}");
        }
    }
}