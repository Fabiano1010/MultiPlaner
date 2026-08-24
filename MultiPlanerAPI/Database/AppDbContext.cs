    using Microsoft.EntityFrameworkCore;
    using MultiPlanerAPI.Models;

    namespace MultiPlanerAPI.Data;

    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<Calendar> Calendars => Set<Calendar>();
        public DbSet<Event> Events => Set<Event>();
        public DbSet<EventData> EventData => Set<EventData>();
        public DbSet<MessageRoom> MessageRooms => Set<MessageRoom>();
        public DbSet<MessageRoomMessages> MessageRoomMessages => Set<MessageRoomMessages>();
        public DbSet<Poll> Polls => Set<Poll>();
        public DbSet<User> Users => Set<User>();
        public DbSet<UserSettings> UserSettings => Set<UserSettings>();
        public DbSet<Menu> Menus => Set<Menu>();
        public DbSet<CalendarList> CalendarLists => Set<CalendarList>();
        public DbSet<CalendarEvent> CalendarEvents => Set<CalendarEvent>();
        public DbSet<CalendarPoll> CalendarPolls => Set<CalendarPoll>();
        public DbSet<CalendarState> CalendarStates => Set<CalendarState>();
        public DbSet<CalendarUser> CalendarUsers => Set<CalendarUser>();
        public DbSet<EventUser> EventUsers => Set<EventUser>();
        public DbSet<PollUser> PollUsers => Set<PollUser>();
        public DbSet<CalendarCalendarList> CalendarCalendarLists => Set<CalendarCalendarList>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.HasDefaultSchema("dbo");

            modelBuilder.Entity<Calendar>(entity =>
            {
                entity.ToTable("calendar");
                entity.HasKey(e => e.Id).HasName("pk_calendar");
                entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
                entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(64).IsRequired();
                entity.Property(e => e.ImageLink).HasColumnName("image_link").HasMaxLength(64).IsRequired();
                entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime").IsRequired();
                entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime").IsRequired();
                entity.Property(e => e.MagicLink).HasColumnName("magic_link").HasMaxLength(128).IsRequired();
            });

            modelBuilder.Entity<User>(entity =>
            {
                entity.ToTable("user");
                entity.HasKey(e => e.Id).HasName("pk_ser");
                entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
                entity.Property(e => e.Username).HasColumnName("username").HasMaxLength(64).IsRequired();
                entity.Property(e => e.Login).HasColumnName("login").HasMaxLength(64).IsRequired();
                entity.Property(e => e.Password).HasColumnName("password").HasMaxLength(64).IsRequired();
                entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime").IsRequired();
                entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime").IsRequired();
                entity.Property(e => e.UserAvatar).HasColumnName("user_avatar").HasMaxLength(64).IsRequired();

                entity.HasIndex(e => e.Login).IsUnique().HasDatabaseName("unq_user");
            });

            modelBuilder.Entity<UserSettings>(entity =>
            {
                entity.ToTable("user_settings");
                entity.HasKey(e => e.IdUser).HasName("pk_user_settings");
                entity.Property(e => e.IdUser).HasColumnName("id_user").ValueGeneratedNever();

                entity.OwnsOne(e => e.Settings, b => b.ToJson());

                entity.HasOne(e => e.User)
                      .WithOne(u => u.UserSettings)
                      .HasForeignKey<UserSettings>(e => e.IdUser)
                      .HasConstraintName("fk_user_settings_user")
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Event>(entity =>
            {
                entity.ToTable("event");
                entity.HasKey(e => e.Id).HasName("pk_Tbl");
                entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
                entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
                entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime").IsRequired();
                entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime").IsRequired();
                entity.Property(e => e.IsHighPriority).HasColumnName("is_high_priority").IsRequired();

                entity.HasOne(e => e.User)
                      .WithMany(u => u.CreatedEvents)
                      .HasForeignKey(e => e.UserId)
                      .HasConstraintName("fk_event_creator_user")
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<EventData>(entity =>
            {
                entity.ToTable("event_data");
                entity.HasKey(e => e.EventId).HasName("pk_event_data");
                entity.Property(e => e.EventId).HasColumnName("event_id").ValueGeneratedNever();
                entity.Property(e => e.StartingDate).HasColumnName("starting_date").HasColumnType("date").IsRequired();
                entity.Property(e => e.EndingDate).HasColumnName("ending_date").HasColumnType("date").IsRequired();
                entity.Property(e => e.Description).HasColumnName("description").HasMaxLength(255);
                entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(64).IsRequired();
                entity.Property(e => e.Color).HasColumnName("color").HasMaxLength(6);

                entity.HasOne(e => e.Event)
                      .WithOne(ev => ev.EventData)
                      .HasForeignKey<EventData>(e => e.EventId)
                      .HasConstraintName("fk_event_data_event")
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<MessageRoom>(entity =>
            {
                entity.ToTable("message_room");
                entity.HasKey(e => e.Id).HasName("pk_message_room");
                entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
                entity.Property(e => e.CalendarId).HasColumnName("calendar_id").IsRequired();

                entity.HasIndex(e => e.CalendarId).IsUnique().HasDatabaseName("unq_message_room");

                entity.HasOne(e => e.Calendar)
                      .WithOne(c => c.MessageRoom)
                      .HasForeignKey<MessageRoom>(e => e.CalendarId)
                      .HasConstraintName("fk_message_room_calendar")
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<MessageRoomMessages>(entity =>
            {
                entity.ToTable("message_room_messages");
                entity.HasKey(e => e.Id).HasName("pk_message_room_messages");
                entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
                entity.Property(e => e.MessageRoomId).HasColumnName("message_room_id").IsRequired();

                entity.HasIndex(e => e.MessageRoomId).IsUnique().HasDatabaseName("unq_message_room_messages");

                entity.OwnsOne(e => e.Messages, b => b.ToJson());

                entity.HasOne(e => e.MessageRoom)
                      .WithOne(mr => mr.MessageRoomMessages)
                      .HasForeignKey<MessageRoomMessages>(e => e.MessageRoomId)
                      .HasConstraintName("fk_message_room_messages_message_room")
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Poll>(entity =>
            {
                entity.ToTable("poll");
                entity.HasKey(e => e.Id).HasName("pk_poll");
                entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
                entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime").IsRequired();
                entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime").IsRequired();

                entity.OwnsOne(e => e.Content, b => b.ToJson());
                entity.OwnsOne(e => e.Result, b => b.ToJson());
            });

            modelBuilder.Entity<Menu>(entity =>
            {
                entity.ToTable("menu");
                entity.HasKey(e => e.Id).HasName("pk_menu");
                entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
                entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();

                entity.HasIndex(e => e.UserId).IsUnique().HasDatabaseName("unq_menu");

                entity.HasOne(e => e.User)
                      .WithOne(u => u.Menu)
                      .HasForeignKey<Menu>(e => e.UserId)
                      .HasConstraintName("fk_menu_user")
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<CalendarList>(entity =>
            {
                entity.ToTable("calendar_list");
                entity.HasKey(e => e.Id).HasName("pk_calendar_list");
                entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
                entity.Property(e => e.CalendarSublistId).HasColumnName("calendar_sublist_id");
                entity.Property(e => e.UserId).HasColumnName("user_id");
                entity.Property(e => e.MenuId).HasColumnName("menu_id").IsRequired();

                entity.HasOne(e => e.CalendarSublist)
                      .WithMany(cl => cl.SubLists)
                      .HasForeignKey(e => e.CalendarSublistId)
                      .HasConstraintName("fk_calendar_list_calendar_list")
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Menu)
                      .WithMany(m => m.CalendarLists)
                      .HasForeignKey(e => e.MenuId)
                      .HasConstraintName("fk_calendar_list_menu")
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.User)
                      .WithMany(u => u.CalendarLists)
                      .HasForeignKey(e => e.UserId)
                      .HasConstraintName("fk_calendar_list_user")
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<CalendarEvent>(entity =>
            {
                entity.ToTable("calendar_event");
                entity.HasKey(e => new { e.CalendarId, e.EventId }).HasName("pk_calendar_event");
                entity.Property(e => e.CalendarId).HasColumnName("calendar_id");
                entity.Property(e => e.EventId).HasColumnName("event_id");

                entity.HasIndex(e => e.EventId).IsUnique().HasDatabaseName("uk_event");

                entity.HasOne(e => e.Calendar)
                      .WithMany(c => c.CalendarEvents)
                      .HasForeignKey(e => e.CalendarId)
                      .HasConstraintName("fk_calendar_event_calendar");

                entity.HasOne(e => e.Event)
                      .WithOne(ev => ev.CalendarEvent)
                      .HasForeignKey<CalendarEvent>(e => e.EventId)
                      .HasConstraintName("fk_calendar_event_event");
            });

            modelBuilder.Entity<CalendarPoll>(entity =>
            {
                entity.ToTable("calendar_poll");
                entity.HasKey(e => new { e.IdCalendar, e.IdPoll }).HasName("pk_calendar_poll");
                entity.Property(e => e.IdCalendar).HasColumnName("id_calendar");
                entity.Property(e => e.IdPoll).HasColumnName("id_poll");

                entity.HasOne(e => e.Calendar)
                      .WithMany(c => c.CalendarPolls)
                      .HasForeignKey(e => e.IdCalendar)
                      .HasConstraintName("fk_calendar_poll_calendar");

                entity.HasOne(e => e.Poll)
                      .WithMany(p => p.CalendarPolls)
                      .HasForeignKey(e => e.IdPoll)
                      .HasConstraintName("fk_calendar_poll_poll");
            });

            modelBuilder.Entity<CalendarState>(entity =>
            {
                entity.ToTable("calendar_state");
                entity.HasKey(e => new { e.CalendarId, e.UserId }).HasName("pk_calendar_state");
                entity.Property(e => e.CalendarId).HasColumnName("calendar_id");
                entity.Property(e => e.UserId).HasColumnName("user_id");

                entity.OwnsOne(e => e.StateContent, b => b.ToJson());

                entity.HasOne(e => e.Calendar)
                      .WithMany(c => c.CalendarStates)
                      .HasForeignKey(e => e.CalendarId)
                      .HasConstraintName("fk_calendar_state_calendar");

                entity.HasOne(e => e.User)
                      .WithMany(u => u.CalendarStates)
                      .HasForeignKey(e => e.UserId)
                      .HasConstraintName("fk_calendar_state_user");
            });

            modelBuilder.Entity<CalendarUser>(entity =>
            {
                entity.ToTable("calendar_user");
                entity.HasKey(e => new { e.UserId, e.CalendarId }).HasName("pk_calendar_user");
                entity.Property(e => e.UserId).HasColumnName("user_id");
                entity.Property(e => e.CalendarId).HasColumnName("calendar_id");
                entity.Property(e => e.UserRole).HasColumnName("user_role").HasMaxLength(64).IsRequired();
                entity.Property(e => e.IsFavourite).HasColumnName("is_favourite").IsRequired();
                entity.Property(e => e.JoinedAt).HasColumnName("joined_at").HasColumnType("datetime").IsRequired();
                entity.Property(e => e.UserAlias).HasColumnName("user_alias").HasMaxLength(64);

                entity.HasOne(e => e.User)
                      .WithMany(u => u.CalendarUsers)
                      .HasForeignKey(e => e.UserId)
                      .HasConstraintName("fk_calendar_user_user");

                entity.HasOne(e => e.Calendar)
                      .WithMany(c => c.CalendarUsers)
                      .HasForeignKey(e => e.CalendarId)
                      .HasConstraintName("fk_calendar_user_calendar");
            });

            modelBuilder.Entity<EventUser>(entity =>
            {
                entity.ToTable("event_user");
                entity.HasKey(e => new { e.UserId, e.EventId }).HasName("pk_event_user");
                entity.Property(e => e.UserId).HasColumnName("user_id");
                entity.Property(e => e.EventId).HasColumnName("event_id");
                entity.Property(e => e.UserRole).HasColumnName("user_role").HasMaxLength(64).IsRequired();

                entity.HasOne(e => e.User)
                      .WithMany(u => u.EventUsers)
                      .HasForeignKey(e => e.UserId)
                      .HasConstraintName("fk_event_user_user");

                entity.HasOne(e => e.Event)
                      .WithMany(ev => ev.EventUsers)
                      .HasForeignKey(e => e.EventId)
                      .HasConstraintName("fk_event_user_event");
            });

            modelBuilder.Entity<PollUser>(entity =>
            {
                entity.ToTable("poll_user");
                entity.HasKey(e => new { e.IdUser, e.IdPoll }).HasName("pk_poll_user");
                entity.Property(e => e.IdUser).HasColumnName("id_user");
                entity.Property(e => e.IdPoll).HasColumnName("id_poll");
                entity.Property(e => e.Voted).HasColumnName("voted").IsRequired();
                entity.Property(e => e.IsOwner).HasColumnName("is_owner").IsRequired();

                entity.HasOne(e => e.User)
                      .WithMany(u => u.PollUsers)
                      .HasForeignKey(e => e.IdUser)
                      .HasConstraintName("fk_poll_user_user");

                entity.HasOne(e => e.Poll)
                      .WithMany(p => p.PollUsers)
                      .HasForeignKey(e => e.IdPoll)
                      .HasConstraintName("fk_poll_user_poll");
            });

            modelBuilder.Entity<CalendarCalendarList>(entity =>
            {
                entity.ToTable("calendar_calendar_list");
                entity.HasKey(e => new { e.CalendarId, e.CalendarListId }).HasName("pk_calendar_calendar_list");
                entity.Property(e => e.CalendarId).HasColumnName("calendar_id");
                entity.Property(e => e.CalendarListId).HasColumnName("calendar_list_id");

                entity.HasOne(e => e.Calendar)
                      .WithMany(c => c.CalendarCalendarLists)
                      .HasForeignKey(e => e.CalendarId)
                      .HasConstraintName("fk_calendar_calendar_list_calendar");

                entity.HasOne(e => e.CalendarList)
                      .WithMany(cl => cl.CalendarCalendarLists)
                      .HasForeignKey(e => e.CalendarListId)
                      .HasConstraintName("fk_calendar_calendar_list_calendar_list");
            });
        }
    }