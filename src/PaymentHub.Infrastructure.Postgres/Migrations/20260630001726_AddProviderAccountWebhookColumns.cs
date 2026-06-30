using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentHub.Infrastructure.Postgres.Migrations
{
    /// <summary>
    /// Slice 2-C — AbacatePay webhook management via API (2026-06-30).
    ///
    /// Adds four NON-SENSITIVE columns to <c>provider_accounts</c> to support
    /// <c>PUT /api/v1/provider-accounts/{providerAccountId}/webhook</c> and
    /// <c>GET /api/v1/provider-accounts/{providerAccountId}/webhook</c>.
    ///
    /// SECURITY NOTES (mirrored in <c>EntityConfigurations.ProviderAccountConfiguration</c>):
    /// - <c>webhook_callback_url</c> targets an absolute URL the merchant
    ///   wants events delivered to. Validated by <c>WebhookUrlValidator</c>
    ///   (HTTPS in production). NOT a secret.
    /// - <c>webhook_events</c> stores a JSON array of event names
    ///   (e.g. <c>["transparent.completed","transparent.refunded"]</c>).
    ///   NOT a secret. Stored as <c>text</c> (NOT <c>jsonb</c>) to preserve
    ///   the exact byte shape inserted by the application — Postgres
    ///   <c>jsonb</c> normalises whitespace on insert (single space after
    ///   each <c>:</c> and <c>,</c>), which would silently mutate the
    ///   column. This mirrors the Slice 3-IT rule for
    ///   <c>webhook_events.raw_payload</c>: any opaque JSON blob that the
    ///   application parses back from a `string` should stay as <c>text</c>.
    ///   See <c>docs/audits/slice-3-it-e2e-api-postgres-outbox-provider-report-2026-06-29.md</c>
    ///   (Anti-Regression Notes, Rule 1).
    /// - <c>webhook_configured_at</c> records the timestamp of the last
    ///   PUT for the operator's audit trail. NOT a secret.
    /// - <c>webhook_remote_status</c> carries the outcome of the upstream
    ///   registration call (<c>NotRegistered</c>, <c>Registered</c>,
    ///   <c>RegistrationFailed</c>, <c>RemoteRegistrationDeferred</c>).
    ///   NOT a secret.
    ///
    /// DO NOT add a column for <c>webhookSecret</c> — the secret is
    /// transported only inside <c>encrypted_credentials</c> as the JSON
    /// field <c>webhookSecret</c> (or legacy <c>secret</c>) and is
    /// protected by <c>ICredentialProtector</c>. Persisting it in a
    /// dedicated column would violate
    /// <c>docs/specs/011-security-and-compliance.md</c>.
    ///
    /// Reference: <c>docs/audits/slice-2c-abacatepay-webhook-management-report-2026-06-29.md</c>.
    /// </summary>
    public partial class AddProviderAccountWebhookColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "webhook_callback_url",
                table: "provider_accounts",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "webhook_configured_at",
                table: "provider_accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "webhook_events",
                table: "provider_accounts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "webhook_remote_status",
                table: "provider_accounts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "webhook_remote_status",
                table: "provider_accounts");

            migrationBuilder.DropColumn(
                name: "webhook_events",
                table: "provider_accounts");

            migrationBuilder.DropColumn(
                name: "webhook_configured_at",
                table: "provider_accounts");

            migrationBuilder.DropColumn(
                name: "webhook_callback_url",
                table: "provider_accounts");
        }
    }
}
