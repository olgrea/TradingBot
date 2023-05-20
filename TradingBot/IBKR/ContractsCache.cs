using IBApi;
using TradingBot.IBKR.Client;

namespace TradingBot.IBKR
{
    internal class ContractsCache
    {
        IBClient _client;
        Dictionary<string, Contract> _cache = new Dictionary<string, Contract>();

        public ContractsCache(IBClient client)
        {
            _client = client;
        }

        public Contract Get(string ticker)
        {
            return Get(ticker, CancellationToken.None);
        }

        public Contract Get(string ticker, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if(!_cache.TryGetValue(ticker, out Contract? contract))
            {
                lock (_cache)
                {
                    token.ThrowIfCancellationRequested();
                    if (!_cache.TryGetValue(ticker, out contract))
                        _cache[ticker] = GetContractAsync(ticker, "SMART", token).Result;
                    else
                        _client.Logger?.Debug($"Contract {contract.ConId} for {ticker} retrieved from cache.");
                }
            }
            else
                _client.Logger?.Debug($"Contract {contract.ConId} for {ticker} retrieved from cache.");

            return _cache[ticker];
        }

        async Task<Contract> GetContractAsync(string symbol, string exchange, CancellationToken token)
        {
            var sampleContract = new Contract()
            {
                Currency = "USD",
                Exchange = exchange,
                Symbol = symbol,
                SecType = "STK"
            };

            var contractDetails = await GetContractDetailsAsync(sampleContract, token);
            _client.Logger?.Trace($"contractDetails : ({string.Join(", ", contractDetails.Select(cd => cd.Contract.ConId.ToString()))})");
            return contractDetails.First().Contract;
        }

        async Task<List<ContractDetails>> GetContractDetailsAsync(Contract contract, CancellationToken token)
        {
            int reqId = -1;

            var tcs = new TaskCompletionSource<List<ContractDetails>>(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(() => tcs.TrySetCanceled());

            var tmpDetails = new List<ContractDetails>();
            var contractDetails = new Action<int, ContractDetails>((rId, c) =>
            {
                if (rId == reqId)
                {
                    tmpDetails.Add(c);
                }
            });
            var contractDetailsEnd = new Action<int>(rId =>
            {
                if (rId == reqId)
                {
                    tcs.SetResult(tmpDetails);
                }
            });
            var error = new Action<ErrorMessage>(msg => tcs.TrySetException(msg));

            _client.Responses.ContractDetails += contractDetails;
            _client.Responses.ContractDetailsEnd += contractDetailsEnd;
            _client.Responses.Error += error;

            try
            {
                _client.Logger?.Debug($"Retrieving contract details for {contract.Symbol}.");
                reqId = _client.RequestContractDetails(contract);
                return await tcs.Task;
            }
            finally
            {
                _client.Responses.ContractDetails -= contractDetails;
                _client.Responses.ContractDetailsEnd -= contractDetailsEnd;
                _client.Responses.Error -= error;
            }
        }
    }
}
