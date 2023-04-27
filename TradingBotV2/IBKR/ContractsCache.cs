using IBApi;

namespace TradingBotV2.IBKR
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
            if (!_cache.ContainsKey(ticker))
            {
                _cache[ticker] = GetContractAsync(ticker).Result;
            }

            return _cache[ticker];
        }

        public async Task<Contract> GetAsync(string ticker)
        {
            if(!_cache.ContainsKey(ticker))
            {
                _cache[ticker] = await GetContractAsync(ticker);
            }

            return _cache[ticker];
        }

        async Task<Contract> GetContractAsync(string symbol, string exchange = "SMART")
        {
            var sampleContract = new Contract()
            {
                Currency = "USD",
                Exchange = exchange,
                Symbol = symbol,
                SecType = "STK"
            };

            var contractDetails = await GetContractDetailsAsync(sampleContract);
            return contractDetails.First().Contract;
        }

        async Task<List<ContractDetails>> GetContractDetailsAsync(Contract contract)
        {
            int reqId = -1;

            var tcs = new TaskCompletionSource<List<ContractDetails>>(TaskCreationOptions.RunContinuationsAsynchronously);
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
