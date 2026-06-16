using FluentAssertions;
using Moq;
using PaymentHub.Application.Abstractions.Context;
using PaymentHub.Application.Abstractions.Outbox;
using PaymentHub.Application.Abstractions.Persistence;
using PaymentHub.Application.Abstractions.Providers;
using PaymentHub.Application.Abstractions.Security;
using PaymentHub.Application.Checkouts;
using PaymentHub.Domain.Entities;
using PaymentHub.Domain.Enums;
using PaymentHub.Domain.ValueObjects;

namespace PaymentHub.UnitTests.Application;

public class CreateCheckoutHandlerTests
{
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<IApplicationClientRepository> _apps = new();
    private readonly Mock<IProviderAccountRepository> _accounts = new();
    private readonly Mock<IPaymentRepository> _payments = new();
    private readonly Mock<IIdempotencyKeyRepository> _idempotency = new();
    private readonly Mock<IIdempotencyRequestHasher> _hasher = new();
    private readonly Mock<IOutboxPublisher> _outbox = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IClock> _clock = new();
    private readonly FakePaymentProviderAdapterStub _fakeAdapter;
    private readonly IPaymentProviderRouter _router;

    public CreateCheckoutHandlerTests()
    {
        _tenants.Setup(t => t.ExistsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _apps.Setup(a => a.GetByTenantAndIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationClient(Guid.NewGuid(), Guid.NewGuid(), "Test App"));
        _accounts.Setup(a => a.GetByCodeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ProviderCode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid t, Guid a, ProviderCode c, CancellationToken _) =>
                new ProviderAccount(Guid.NewGuid(), t, a, c, ProviderEnvironment.Sandbox, "Fake", "encrypted"));
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _clock.Setup(c => c.UtcNow).Returns(new DateTime(2026, 6, 16, 12, 0, 0, DateTimeKind.Utc));
        _hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("hash");

        _fakeAdapter = new FakePaymentProviderAdapterStub();
        _router = new TestProviderRouter(_fakeAdapter);
    }

    [Fact]
    public async Task HandleAsync_ShouldCreatePaymentAndReturnPending()
    {
        var handler = CreateHandler();
        var tenantId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        _apps.Setup(a => a.GetByTenantAndIdAsync(tenantId, appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationClient(appId, tenantId, "Test App"));

        var request = new CreateCheckoutRequestDto(
            "order-1",
            new CustomerDto("Customer", "customer@example.com"),
            new List<CheckoutItemDto>
            {
                new("premium-monthly", "Plano Premium", 1, 2990)
            },
            "BRL",
            "https://example.com/success",
            "https://example.com/cancel",
            new Dictionary<string, string> { ["source"] = "job-search" });

        var result = await handler.HandleAsync(
            tenantId, appId, "key-1", request, null, CancellationToken.None);

        result.Status.Should().Be("Pending");
        result.Provider.Should().Be("Fake");
        result.CheckoutUrl.Should().StartWith("https://fake-checkout.local/payments/");
        result.PaymentId.Should().NotBeEmpty();

        _payments.Verify(p => p.AddAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>()), Times.Once);
        _idempotency.Verify(i => i.AddAsync(It.IsAny<IdempotencyKey>(), It.IsAny<CancellationToken>()), Times.Once);
        _outbox.Verify(o => o.EnqueueAsync(
            tenantId, appId, "payment.checkout.created",
            It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_ShouldReturnSamePaymentForRepeatedIdempotencyKey()
    {
        var handler = CreateHandler();
        var tenantId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var existingPaymentId = Guid.NewGuid();
        _apps.Setup(a => a.GetByTenantAndIdAsync(tenantId, appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationClient(appId, tenantId, "Test App"));
        _idempotency.Setup(i => i.FindAsync(tenantId, appId, "key-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdempotencyKey(Guid.NewGuid(), tenantId, appId, "key-1", "hash", existingPaymentId));
        _payments.Setup(p => p.GetByIdAsync(existingPaymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Payment(
                existingPaymentId, tenantId, appId, "order-1",
                Money.Of(2990, "BRL"), ProviderCode.Fake, null, null, null, null));

        var request = new CreateCheckoutRequestDto(
            "order-1",
            new CustomerDto(null, null),
            new List<CheckoutItemDto> { new("a", "A", 1, 2990) },
            "BRL", null, null, null);

        var result = await handler.HandleAsync(
            tenantId, appId, "key-1", request, null, CancellationToken.None);

        result.PaymentId.Should().Be(existingPaymentId);
        _payments.Verify(p => p.AddAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ShouldThrowWithoutIdempotencyKey()
    {
        var handler = CreateHandler();
        var request = new CreateCheckoutRequestDto(
            "order-1", new CustomerDto(null, null),
            new List<CheckoutItemDto> { new("a", "A", 1, 100) },
            "BRL", null, null, null);

        var act = async () => await handler.HandleAsync(
            Guid.NewGuid(), Guid.NewGuid(), " ", request, null, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task HandleAsync_ShouldThrowIfItemsEmpty()
    {
        var handler = CreateHandler();
        var request = new CreateCheckoutRequestDto(
            "order-1", new CustomerDto(null, null),
            new List<CheckoutItemDto>(),
            "BRL", null, null, null);

        var act = async () => await handler.HandleAsync(
            Guid.NewGuid(), Guid.NewGuid(), "key", request, null, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private CreateCheckoutHandler CreateHandler()
        => new(_tenants.Object, _apps.Object, _accounts.Object, _payments.Object,
            _idempotency.Object, _hasher.Object, _router, _outbox.Object, _uow.Object, _clock.Object);

    private sealed class FakePaymentProviderAdapterStub : IPaymentProviderAdapter
    {
        public string ProviderCode => "Fake";

        public Task<CreateCheckoutProviderResult> CreateCheckoutAsync(
            CreateCheckoutProviderRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new CreateCheckoutProviderResult(
                Success: true,
                ProviderPaymentId: $"fake_{request.PaymentId:N}",
                CheckoutUrl: $"https://fake-checkout.local/payments/{request.PaymentId}",
                ErrorMessage: null));
        }

        public Task<ProviderWebhookParseResult> ParseWebhookAsync(
            ProviderWebhookRequest request, CancellationToken cancellationToken)
            => throw new NotImplementedException();
    }

    private sealed class TestProviderRouter : IPaymentProviderRouter
    {
        private readonly IPaymentProviderAdapter _adapter;
        public TestProviderRouter(IPaymentProviderAdapter adapter) => _adapter = adapter;
        public IPaymentProviderAdapter Resolve(string? requestedProviderCode) => _adapter;
    }
}
