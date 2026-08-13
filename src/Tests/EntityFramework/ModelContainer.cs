using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ApprovalTests.Tests.EntityFramework
{
    public class Company
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Website { get; set; }
    }

    public class Employee
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int? Boss { get; set; }
        public int? Company { get; set; }
    }

    public class Job
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public int Employee { get; set; }
        public string Status { get; set; }
    }

    public class Event
    {
        public int Id { get; set; }
        public int? Employee { get; set; }
        public string EventTitle { get; set; }
        public string Details { get; set; }
    }

    public class ModelContainer : DbContext
    {
        private const string ConnectionString = "Data Source=file:ApprovalTestsEntityFrameworkDemo?mode=memory&cache=shared";
        private static readonly SqliteConnection KeepAliveConnection = CreateKeepAliveConnection();
        private static readonly object EnsureCreatedLock = new object();
        private static bool created;

        public DbSet<Company> Companies { get; set; }
        public DbSet<Employee> Employees { get; set; }
        public DbSet<Job> Jobs { get; set; }
        public DbSet<Event> Events { get; set; }

        public ModelContainer()
        {
            lock (EnsureCreatedLock)
            {
                if (!created)
                {
                    Database.EnsureCreated();
                    created = true;
                }
            }
        }

        private static SqliteConnection CreateKeepAliveConnection()
        {
            var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            return connection;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite(ConnectionString);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Company>().ToTable("Company").HasData(
                new Company { Id = 84, Name = "Microsoft", Website = "www.bing.com" });

            modelBuilder.Entity<Employee>().ToTable("Employee").HasData(
                new Employee { Id = 92, Name = "Lynn", Boss = 93, Company = 84 },
                new Employee { Id = 93, Name = "Steve", Boss = null, Company = 84 });

            modelBuilder.Entity<Job>().ToTable("Job").HasData(
                new Job { Id = 81, Title = "Developer", Employee = 92, Status = "old" },
                new Job { Id = 82, Title = "SqlAzure Evanglist", Employee = 92, Status = "current" });

            modelBuilder.Entity<Event>().ToTable("Events").HasData(
                new Event { Id = 69, Employee = null, EventTitle = "SxSW", Details = null },
                new Event { Id = 70, Employee = 92, EventTitle = "Sql VUG", Details = null });
        }
    }
}
