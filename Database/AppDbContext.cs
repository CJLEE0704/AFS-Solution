using Microsoft.EntityFrameworkCore;

namespace PipeBendingDashboard.Database
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<MachineEntity>          Machines          { get; set; } = null!;
        public DbSet<MachineStatusLogEntity> MachineStatusLogs { get; set; } = null!;
        public DbSet<CommLogEntity>          CommLogs          { get; set; } = null!;
        public DbSet<ProjectEntity>          Projects          { get; set; } = null!;
        public DbSet<PipeEntity>             Pipes             { get; set; } = null!;
        public DbSet<ProductionRecordEntity> ProductionRecords { get; set; } = null!;
        public DbSet<PipeMachineLogEntity>   PipeMachineLogs   { get; set; } = null!;
        public DbSet<UserEntity>             Users             { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // machine_status_log: 외래키 설정 (cascade 없음)
            modelBuilder.Entity<MachineStatusLogEntity>()
                .HasOne<MachineEntity>()
                .WithMany()
                .HasForeignKey(x => x.MachineId)
                .OnDelete(DeleteBehavior.Restrict);

            // pipes: project 외래키
            modelBuilder.Entity<PipeEntity>()
                .HasOne<ProjectEntity>()
                .WithMany()
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);

            // production_records: pipe, project 외래키
            modelBuilder.Entity<ProductionRecordEntity>()
                .HasOne<PipeEntity>()
                .WithMany()
                .HasForeignKey(x => x.PipeId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ProductionRecordEntity>()
                .HasOne<ProjectEntity>()
                .WithMany()
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
