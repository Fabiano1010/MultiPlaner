using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Sqlite;
using MultiPlanerAPI.Models;

namespace MultiPlanerAPI.Data;



public class AppDbContext : DbContext
{
 public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
 {
  
 }
    
    public DbSet<Event> Events { get; set; }
}