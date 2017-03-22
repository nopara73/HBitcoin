# HBitcoin
Privacy focused Bitcoin library on top of NBitcoin for .NET Core.

## [Documentation on CodeProject](https://www.codeproject.com/Articles/1096320/HBitcoin-High-level-Csharp-Bitcoin-Wallet-Library)

## [Nuget](https://www.nuget.org/packages/HBitcoin)

## Build & Test

1. `git clone https://github.com/nopara73/HBitcoin`
2. `cd HBitcoin/`
3. `dotnet restore`
4. `cd src/HBitcoin.Tests/`
5. `dotnet test`

*Notes:* 
- As of today some tests might fail when running them all at once. Running them one by one should work.
- Some tests have been prefunded with testnet coins. If some funny dev funds the messing with the wallets or the coins run out due to the fees, some tests are burn some tests might fail, too.
