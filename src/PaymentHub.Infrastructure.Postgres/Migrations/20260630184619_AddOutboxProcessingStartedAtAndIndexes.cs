using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentHub.Infrastructure.Postgres.Migrations
{
    /// <summary>
    /// Slice 7-M1 — Outbox multi-instance support (2026-06-30).
    ///
    /// Adds <c>processing_started_at</c> to <c>outbox_events</c> and replaces the legacy
    /// <c>(status, next_retry_at)</c> claim index with one that also covers the
    /// <c>ORDER BY created_at</c> the new <c>ClaimPendingForDispatchAsync</c> relies on.
    /// A second, partial index supports the orphan sweep
    /// (<c>SweepOrphanedProcessingAsync</c>) by efficiently finding rows still in
    /// <c>Processing</c> past the configured TTL.
    ///
    /// SECURITY / ANTI-REGRESSION NOTES (mirrored in
    /// <c>EntityConfigurations.OutboxEventConfiguration</c>):
    /// - <c>processing_started_at</c> is a non-sensitive timestamp. It carries no URL,
    ///   signature, secret, response body or provider data. Safe to log/audit.
    /// - <c>outbox_events.payload</c> stays <c>jsonb</c> (Slice 7-IT rule). This slice does
    ///   NOT touch <c>payload</c> or <c>webhook_events.raw_payload</c>. <c>raw_payload</c>
    ///   stays <c>text</c> for byte-exact HMAC preservation (Slice 3-IT rule).
    /// - The orphan sweep persists only the safe category
    ///   <see cref="PaymentHub.Domain.Enums.WebhookDispatcherCategory.ProcessingOrphaned"/>
    ///   to <c>last_error</c>; never the original exception, URL, body or signature.
    ///
    /// Reference: <c>docs/audits/slice-7-m1-outbox-multi-instance-report-2026-06-30.md</c>
    /// and the "Multi-instancia (Slice 7-M1)" section of
    /// <c>docs/specs/007-inbox-outbox-workers.md</c>.
    /// </summary>
    public partial class AddOutboxProcessingStartedAtAndIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_outbox_events_status_next_retry_at",
                table: "outbox_events");

            migrationBuilder.AddColumn<DateTime>(
                name: "processing_started_at",
                table: "outbox_events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_events_status_next_retry_at_created_at",
                table: "outbox_events",
                columns: new[] { "status", "next_retry_at", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_events_status_processing_started_at",
                table: "outbox_events",
                columns: new[] { "status", "processing_started_at" },
                filter: "status = 'Processing'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_outbox_events_status_next_retry_at_created_at",
                table: "outbox_events");

            migrationBuilder.DropIndex(
                name: "IX_outbox_events_status_processing_started_at",
                table: "outbox_events");

            migrationBuilder.DropColumn(
                name: "processing_started_at",
                table: "outbox_events");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_events_status_next_retry_at",
                table: "outbox_events",
                columns: new[] { "status", "next_retry_at" });
        }
    }
}
