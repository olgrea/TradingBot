using NUnit.Framework;
using TradingBotV2.Broker;
using TradingBotV2.Broker.Accounts;
using TradingBotV2.IBKR;
using TradingBotV2.IBKR.Client;
using TradingBotV2.Tests;

namespace IBBrokerTests
{
    [TestFixture]
    public class BrokerTests
    {
        protected string _accountCode;
        protected IBroker _broker;
        protected ILogger _logger;

        [SetUp]
        public virtual async Task SetUp()
        {
            _logger = TestsUtils.CreateLogger();
            _broker = TestsUtils.CreateBroker(_logger);
            _accountCode = await _broker.ConnectAsync();
        }

        [TearDown]
        public virtual async Task TearDown()
        {
            await Task.Delay(50);
            await _broker.DisconnectAsync();
            await Task.Delay(50);
        }

        [Test]
        public async Task ConnectAsync_OnSuccess_ReturnsAccountCode()
        {
            var result = await _broker.ConnectAsync();
            Assert.NotNull(result);
        }

        [Test]
        public async Task ConnectAsync_AlreadyConnected_ThrowsError()
        {
            await _broker.ConnectAsync();
            Assert.ThrowsAsync<ErrorMessage>(async () => await _broker.ConnectAsync());
        }

        [Test]
        public async Task GetAccountAsync_WithValidAccountCode_GetsTheAccount()
        {
            string accountCode = await _broker.ConnectAsync();

            Account account = await _broker.GetAccountAsync(accountCode);
            Assert.IsNotNull(account);
            Assert.AreEqual(accountCode, account.Code);
            Assert.AreNotEqual(account.Time, default(DateTime));
        }

        [Test]
        public async Task GetAccountAsync_NoAccountCodeProvided_SingleAccountStructure_GetsTheDefaultAccount()
        {
            string accountCode = await _broker.ConnectAsync();

            Account account = await _broker.GetAccountAsync(accountCode);
            Assert.IsNotNull(account);
            Assert.AreEqual(accountCode, account.Code);
            Assert.AreNotEqual(account.Time, default(DateTime));
            Assert.True(account.CashBalances.Any());
        }
    }
}
