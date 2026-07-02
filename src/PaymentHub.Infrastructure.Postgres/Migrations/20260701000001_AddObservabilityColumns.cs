using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentHub.Infrastructure.Postgres.Migrations
{
    /// <summary>
    /// Slice 9-O1.2 — Observabilidade mínima (2026-07-01).
    ///
    /// Adds <c>correlation_id</c> to <c>webhook_events</c> and
    /// <c>outbox_events</c> so the request-scoped id resolved by
    /// <c>CorrelationIdMiddleware</c> propagates end-to-end through the
    /// Inbox → Outbox → Dispatcher pipeline without polluting the JSON
    /// payload (which stays <c>jsonb</c> on <c>outbox_events.payload</c>
    /// and <c>text</c> on <c>webhook_events.raw_payload</c>).
    ///
    /// <para><b>Schema</b></para>
    /// <list type="bullet">
    /// <item><c>webhook_events.correlation_id VARCHAR(64) NULL</c></item>
    /// <item><c>outbox_events.correlation_id VARCHAR(64) NULL</c></item>
    /// </list>
    ///
    /// <para><b>Security / anti-regression notes</b> (mirrored in
    /// <c>EntityConfigurations.WebhookEventConfiguration</c> and
    /// <c>EntityConfigurations.OutboxEventConfiguration</c>):</para>
    /// <list type="bullet">
    /// <item><c>correlation_id</c> is bounded to 64 chars to match the
    /// Domain-layer <c>NormalizeCorrelationId</c> cap and the middleware
    /// regex <c>^[A-Za-z0-9\-]{8,128}$</c>. Out-of-range values are
    /// silently truncated before persist.</item>
    /// <item>The column is nullable because legacy rows (pre-Slice 9-O1)
    /// and background seeds have no inbound request id.</item>
    /// <item>The column does NOT replace any payload column. The
    /// payload-bearing columns remain unchanged:
    /// <c>webhook_events.raw_payload text</c> (Slice 3-IT) and
    /// <c>outbox_events.payload jsonb</c> (Slice 7-IT).</item>
    /// <item>This migration does NOT add any index on <c>correlation_id</c>.
    /// Operational queries by correlation id are still expected to be rare
    /// (incident triage, not steady state). Indexing is deferred until a
    /// measured workload justifies it (Slice 7-M1 documented the same
    /// pattern for <c>processing_started_at</c>).</item>
    /// </list>
    /// </summary>
    public partial class AddObservabilityColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "correlation_id",
                table: "webhook_events",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "correlation_id",
                table: "outbox_events",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "correlation_id",
                table: "webhook_events");

            migrationBuilder.DropColumn(
                name: "correlation_id",
                table: "outbox_events");
        }
    }
}
