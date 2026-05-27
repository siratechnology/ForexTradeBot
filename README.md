# ForexTradeBot

XAUUSD (Gold/USD) trading bot powered by Claude Opus 4.7. Runs in paper trading mode or live via MetaAPI + MT5.

## Quick start

```bash
cp .env.example .env
# edit .env with your keys

docker compose up --build
```

## Environment variables

| Variable | Required | Description |
|---|---|---|
| `ANTHROPIC_API_KEY` | Yes | Claude API key from console.anthropic.com |
| `META_API_TOKEN` | No | MetaAPI token — enables live MT5 trading |
| `META_ACCOUNT_ID` | No | MetaAPI account ID |

Without `META_API_TOKEN` / `META_ACCOUNT_ID` the bot runs in **paper trading** mode (simulated prices, no real money).

## Server deployment (Linux AMD64)

```bash
git clone git@github.com:siratechnology/ForexTradeBot.git
cd ForexTradeBot
cp .env.example .env
nano .env          # paste your keys

docker build -t forextradebot .
docker run --env-file .env forextradebot
```

Run 10 cycles:
```bash
docker run --env-file .env forextradebot 10
```
