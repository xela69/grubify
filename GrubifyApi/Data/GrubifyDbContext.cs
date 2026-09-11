using Microsoft.EntityFrameworkCore;
using GrubifyApi.Models;

namespace GrubifyApi.Data
{
    public class GrubifyDbContext : DbContext
    {
        public GrubifyDbContext(DbContextOptions<GrubifyDbContext> options) : base(options) { }

        public DbSet<Restaurant> Restaurants => Set<Restaurant>();
        public DbSet<FoodItem> FoodItems => Set<FoodItem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Restaurant>(e =>
            {
                e.HasKey(r => r.Id);
                e.Property(r => r.Id).ValueGeneratedNever(); // preserve seed Ids so FoodItem.RestaurantId links hold
                e.Ignore(r => r.MenuItems);
                e.Property(r => r.DeliveryFee).HasPrecision(10, 2);
                e.Property(r => r.MinimumOrder).HasPrecision(10, 2);
            });

            modelBuilder.Entity<FoodItem>(e =>
            {
                e.HasKey(f => f.Id);
                e.Property(f => f.Id).ValueGeneratedNever();
                e.Property(f => f.Price).HasPrecision(10, 2);
            });
        }
    }
}
