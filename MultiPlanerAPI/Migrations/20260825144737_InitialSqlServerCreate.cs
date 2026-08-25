using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MultiPlanerAPI.Migrations
{
    /// <inheritdoc />
    public partial class InitialSqlServerCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.CreateTable(
                name: "calendar",
                schema: "dbo",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    image_link = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    magic_link = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendar", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "poll",
                schema: "dbo",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    result = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_poll", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user",
                schema: "dbo",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    username = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    login = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    password = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    user_avatar = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ser", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "message_room",
                schema: "dbo",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    calendar_id = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_message_room", x => x.id);
                    table.ForeignKey(
                        name: "fk_message_room_calendar",
                        column: x => x.calendar_id,
                        principalSchema: "dbo",
                        principalTable: "calendar",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "calendar_poll",
                schema: "dbo",
                columns: table => new
                {
                    id_calendar = table.Column<int>(type: "int", nullable: false),
                    id_poll = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendar_poll", x => new { x.id_calendar, x.id_poll });
                    table.ForeignKey(
                        name: "fk_calendar_poll_calendar",
                        column: x => x.id_calendar,
                        principalSchema: "dbo",
                        principalTable: "calendar",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_calendar_poll_poll",
                        column: x => x.id_poll,
                        principalSchema: "dbo",
                        principalTable: "poll",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "calendar_state",
                schema: "dbo",
                columns: table => new
                {
                    calendar_id = table.Column<int>(type: "int", nullable: false),
                    user_id = table.Column<int>(type: "int", nullable: false),
                    state_content = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendar_state", x => new { x.calendar_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_calendar_state_calendar",
                        column: x => x.calendar_id,
                        principalSchema: "dbo",
                        principalTable: "calendar",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_calendar_state_user",
                        column: x => x.user_id,
                        principalSchema: "dbo",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "calendar_user",
                schema: "dbo",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "int", nullable: false),
                    calendar_id = table.Column<int>(type: "int", nullable: false),
                    user_role = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    is_favourite = table.Column<bool>(type: "bit", nullable: false),
                    joined_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    user_alias = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendar_user", x => new { x.user_id, x.calendar_id });
                    table.ForeignKey(
                        name: "fk_calendar_user_calendar",
                        column: x => x.calendar_id,
                        principalSchema: "dbo",
                        principalTable: "calendar",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_calendar_user_user",
                        column: x => x.user_id,
                        principalSchema: "dbo",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "event",
                schema: "dbo",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    user_id = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime", nullable: false),
                    is_high_priority = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_Tbl", x => x.id);
                    table.ForeignKey(
                        name: "fk_event_creator_user",
                        column: x => x.user_id,
                        principalSchema: "dbo",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "menu",
                schema: "dbo",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    user_id = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_menu", x => x.id);
                    table.ForeignKey(
                        name: "fk_menu_user",
                        column: x => x.user_id,
                        principalSchema: "dbo",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "poll_user",
                schema: "dbo",
                columns: table => new
                {
                    id_user = table.Column<int>(type: "int", nullable: false),
                    id_poll = table.Column<int>(type: "int", nullable: false),
                    voted = table.Column<bool>(type: "bit", nullable: false),
                    is_owner = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_poll_user", x => new { x.id_user, x.id_poll });
                    table.ForeignKey(
                        name: "fk_poll_user_poll",
                        column: x => x.id_poll,
                        principalSchema: "dbo",
                        principalTable: "poll",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_poll_user_user",
                        column: x => x.id_user,
                        principalSchema: "dbo",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_settings",
                schema: "dbo",
                columns: table => new
                {
                    id_user = table.Column<int>(type: "int", nullable: false),
                    Settings = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_settings", x => x.id_user);
                    table.ForeignKey(
                        name: "fk_user_settings_user",
                        column: x => x.id_user,
                        principalSchema: "dbo",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "message_room_messages",
                schema: "dbo",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    message_room_id = table.Column<int>(type: "int", nullable: false),
                    messages = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_message_room_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_message_room_messages_message_room",
                        column: x => x.message_room_id,
                        principalSchema: "dbo",
                        principalTable: "message_room",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "calendar_event",
                schema: "dbo",
                columns: table => new
                {
                    calendar_id = table.Column<int>(type: "int", nullable: false),
                    event_id = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendar_event", x => new { x.calendar_id, x.event_id });
                    table.ForeignKey(
                        name: "fk_calendar_event_calendar",
                        column: x => x.calendar_id,
                        principalSchema: "dbo",
                        principalTable: "calendar",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_calendar_event_event",
                        column: x => x.event_id,
                        principalSchema: "dbo",
                        principalTable: "event",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "event_data",
                schema: "dbo",
                columns: table => new
                {
                    event_id = table.Column<int>(type: "int", nullable: false),
                    starting_date = table.Column<DateOnly>(type: "date", nullable: false),
                    ending_date = table.Column<DateOnly>(type: "date", nullable: false),
                    description = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    color = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_data", x => x.event_id);
                    table.ForeignKey(
                        name: "fk_event_data_event",
                        column: x => x.event_id,
                        principalSchema: "dbo",
                        principalTable: "event",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "event_user",
                schema: "dbo",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "int", nullable: false),
                    event_id = table.Column<int>(type: "int", nullable: false),
                    user_role = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_user", x => new { x.user_id, x.event_id });
                    table.ForeignKey(
                        name: "fk_event_user_event",
                        column: x => x.event_id,
                        principalSchema: "dbo",
                        principalTable: "event",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_event_user_user",
                        column: x => x.user_id,
                        principalSchema: "dbo",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "calendar_list",
                schema: "dbo",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    calendar_sublist_id = table.Column<int>(type: "int", nullable: true),
                    user_id = table.Column<int>(type: "int", nullable: true),
                    menu_id = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendar_list", x => x.id);
                    table.ForeignKey(
                        name: "fk_calendar_list_calendar_list",
                        column: x => x.calendar_sublist_id,
                        principalSchema: "dbo",
                        principalTable: "calendar_list",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_calendar_list_menu",
                        column: x => x.menu_id,
                        principalSchema: "dbo",
                        principalTable: "menu",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_calendar_list_user",
                        column: x => x.user_id,
                        principalSchema: "dbo",
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "calendar_calendar_list",
                schema: "dbo",
                columns: table => new
                {
                    calendar_id = table.Column<int>(type: "int", nullable: false),
                    calendar_list_id = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendar_calendar_list", x => new { x.calendar_id, x.calendar_list_id });
                    table.ForeignKey(
                        name: "fk_calendar_calendar_list_calendar",
                        column: x => x.calendar_id,
                        principalSchema: "dbo",
                        principalTable: "calendar",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_calendar_calendar_list_calendar_list",
                        column: x => x.calendar_list_id,
                        principalSchema: "dbo",
                        principalTable: "calendar_list",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_calendar_calendar_list_calendar_list_id",
                schema: "dbo",
                table: "calendar_calendar_list",
                column: "calendar_list_id");

            migrationBuilder.CreateIndex(
                name: "uk_event",
                schema: "dbo",
                table: "calendar_event",
                column: "event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_calendar_list_calendar_sublist_id",
                schema: "dbo",
                table: "calendar_list",
                column: "calendar_sublist_id");

            migrationBuilder.CreateIndex(
                name: "IX_calendar_list_menu_id",
                schema: "dbo",
                table: "calendar_list",
                column: "menu_id");

            migrationBuilder.CreateIndex(
                name: "IX_calendar_list_user_id",
                schema: "dbo",
                table: "calendar_list",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_calendar_poll_id_poll",
                schema: "dbo",
                table: "calendar_poll",
                column: "id_poll");

            migrationBuilder.CreateIndex(
                name: "IX_calendar_state_user_id",
                schema: "dbo",
                table: "calendar_state",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_calendar_user_calendar_id",
                schema: "dbo",
                table: "calendar_user",
                column: "calendar_id");

            migrationBuilder.CreateIndex(
                name: "IX_event_user_id",
                schema: "dbo",
                table: "event",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_event_user_event_id",
                schema: "dbo",
                table: "event_user",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "unq_menu",
                schema: "dbo",
                table: "menu",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "unq_message_room",
                schema: "dbo",
                table: "message_room",
                column: "calendar_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "unq_message_room_messages",
                schema: "dbo",
                table: "message_room_messages",
                column: "message_room_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_poll_user_id_poll",
                schema: "dbo",
                table: "poll_user",
                column: "id_poll");

            migrationBuilder.CreateIndex(
                name: "unq_user",
                schema: "dbo",
                table: "user",
                column: "login",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "calendar_calendar_list",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "calendar_event",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "calendar_poll",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "calendar_state",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "calendar_user",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "event_data",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "event_user",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "message_room_messages",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "poll_user",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "user_settings",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "calendar_list",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "event",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "message_room",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "poll",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "menu",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "calendar",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "user",
                schema: "dbo");
        }
    }
}
