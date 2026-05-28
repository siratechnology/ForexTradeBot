# GoldBot iOS App — Claude Code Prompt

> Paste everything below the horizontal rule into Claude Code on macOS to generate the full Swift app.

---

Create a complete SwiftUI iOS app called **GoldBot** in Xcode.  
Target: iOS 17+. No external dependencies. No storyboards.

Create these files: `GoldBotApp.swift`, `ContentView.swift`, `APIService.swift`, `Models.swift`

## API Overview

- Base URL stored in `AppStorage("serverURL")`
- Personal key stored in `AppStorage("apiKey")`
- Every request sends header: `X-Api-Key: {apiKey}`
- Server: `https://forex.siratechnology.com`

---

## Models.swift

```swift
struct BotStatus: Codable {
    let label: String?
    let isRunning: Bool
    let isInitialized: Bool
    let price: Double
    let floatPnL: Double
    let hasPosition: Bool
    let tradesToday: Int
    let lastAction: String
    let serverTimeUtc: String
    let settings: BotSettings
}

struct BotSettings: Codable {
    var lotSize: Double
    var maxTradesPerDay: Int
    var tradingStartUtc: String?
    var tradingEndUtc: String?
}

struct OpenPosition: Codable, Identifiable {
    let id: String
    let type: String        // "BUY" or "SELL"
    let lots: Double
    let openPrice: Double
    let pnL: Double
}

struct TradeRecord: Codable, Identifiable {
    let time: String
    let action: String
    let lots: Double
    let price: Double
    let reason: String
    var id: String { time + action }
}

struct PositionsResponse: Codable {
    let positions: [OpenPosition]
    let count: Int
}

struct TradesResponse: Codable {
    let trades: [TradeRecord]
}

struct RegisterResponse: Codable {
    let apiKey: String?
    let label: String?
    let message: String?
    let error: String?
}

struct TradeRequest: Codable { let lots: Double }
struct ActionResponse: Codable {
    let result: String?
    let message: String?
    let error: String?
}
```

---

## APIService.swift

```swift
@MainActor
class APIService: ObservableObject {
    static let shared = APIService()

    @AppStorage("serverURL") var serverURL = ""
    @AppStorage("apiKey")    var apiKey    = ""

    // Called once during onboarding.
    // metaToken + metaAccountId are sent to server and NOT stored on device.
    // Returns the personal apiKey on success.
    func register(
        serverURL: String,
        registrationCode: String,
        metaToken: String,
        metaAccountId: String,
        label: String
    ) async throws -> String {
        let url = URL(string: "\(serverURL)/api/accounts")!
        var req = URLRequest(url: url, timeoutInterval: 30)
        req.httpMethod = "POST"
        req.setValue(registrationCode, forHTTPHeaderField: "X-Api-Key")
        req.setValue("application/json", forHTTPHeaderField: "Content-Type")
        let body = ["label": label, "metaToken": metaToken, "metaAccountId": metaAccountId]
        req.httpBody = try JSONEncoder().encode(body)
        let (data, resp) = try await URLSession.shared.data(for: req)
        guard (resp as? HTTPURLResponse)?.statusCode == 200 else {
            let err = try? JSONDecoder().decode(RegisterResponse.self, from: data)
            throw APIError.serverError(err?.error ?? "Registration failed (\((resp as? HTTPURLResponse)?.statusCode ?? 0))")
        }
        let result = try JSONDecoder().decode(RegisterResponse.self, from: data)
        guard let key = result.apiKey, !key.isEmpty else {
            throw APIError.serverError("No API key returned")
        }
        return key
    }

    func getStatus()    async throws -> BotStatus          { try await get("/api/status") }
    func getPositions() async throws -> PositionsResponse  { try await get("/api/status/positions") }
    func getTrades()    async throws -> TradesResponse     { try await get("/api/status/trades?limit=30") }

    func startBot()               async throws -> ActionResponse { try await post("/api/bot/start",    body: Empty()) }
    func stopBot()                async throws -> ActionResponse { try await post("/api/bot/stop",     body: Empty()) }
    func buy(lots: Double)        async throws -> ActionResponse { try await post("/api/trade/buy",    body: TradeRequest(lots: lots)) }
    func sell(lots: Double)       async throws -> ActionResponse { try await post("/api/trade/sell",   body: TradeRequest(lots: lots)) }
    func closeAll()               async throws -> ActionResponse { try await post("/api/trade/close",  body: Empty()) }
    func updateSettings(_ s: BotSettings) async throws -> ActionResponse { try await put("/api/settings", body: s) }

    // MARK: - Internals

    private struct Empty: Encodable {}

    private func get<T: Decodable>(_ path: String) async throws -> T {
        var req = URLRequest(url: URL(string: serverURL + path)!, timeoutInterval: 15)
        req.setValue(apiKey, forHTTPHeaderField: "X-Api-Key")
        let (data, _) = try await URLSession.shared.data(for: req)
        return try JSONDecoder().decode(T.self, from: data)
    }

    private func post<T: Decodable, B: Encodable>(_ path: String, body: B) async throws -> T {
        try await send("POST", path: path, body: body)
    }

    private func put<T: Decodable, B: Encodable>(_ path: String, body: B) async throws -> T {
        try await send("PUT", path: path, body: body)
    }

    private func send<T: Decodable, B: Encodable>(_ method: String, path: String, body: B) async throws -> T {
        var req = URLRequest(url: URL(string: serverURL + path)!, timeoutInterval: 15)
        req.httpMethod = method
        req.setValue(apiKey, forHTTPHeaderField: "X-Api-Key")
        req.setValue("application/json", forHTTPHeaderField: "Content-Type")
        req.httpBody = try JSONEncoder().encode(body)
        let (data, _) = try await URLSession.shared.data(for: req)
        return try JSONDecoder().decode(T.self, from: data)
    }
}

enum APIError: LocalizedError {
    case serverError(String)
    var errorDescription: String? {
        if case .serverError(let msg) = self { return msg }
        return "Unknown error"
    }
}
```

---

## ContentView.swift — Layout Spec

Use `@StateObject var api = APIService.shared`.  
Auto-refresh every 10 seconds with `Timer.publish(every: 10, on: .main, in: .common)`.

### Setup Sheet (shown when `apiKey.isEmpty`)

Full-screen sheet shown on first launch or after disconnect.

**Fields:**
| Field | Type | Placeholder |
|-------|------|-------------|
| Server URL | TextField | `https://forex.siratechnology.com` |
| Registration Code | SecureField | given by server owner |
| MetaAPI Token | SecureField | from metaapi.cloud dashboard |
| MetaAPI Account ID | TextField | from metaapi.cloud → Accounts |
| Your Name / Label | TextField | e.g. John Live Account |

**Connect button behavior:**
1. Show `ProgressView` while loading
2. Call `api.register(serverURL, registrationCode, metaToken, accountId, label)`
3. On success → save `apiKey` + `serverURL` to AppStorage → dismiss sheet
4. On error → show red error message below button

**Info text below button:**
> "Your MetaAPI credentials are sent securely to the server and never stored on this device. You receive a personal access key instead."

---

### Main Dashboard (ScrollView)

#### 1. Header
- Navigation title: `"XAUUSD Gold Bot"`
- Subtitle: account label from status response
- Status dot: 🟢 running+initialized · 🟡 paused · 🔴 error/offline
- Top-right gear icon → Settings sheet

#### 2. Price Card (gold/yellow accent color)
- Large price text: `$3,342.50`
- Float P&L with color: green if positive, red if negative (`+$2.30`)
- `lastAction` in gray small font
- Trades today: `X / max` or `X / ∞`

#### 3. Bot Control Row
- `[▶ Start]` green button — disabled when `isRunning == true`
- `[⏹ Stop]` red button — disabled when `isRunning == false`
- Show `"Initializing..."` label when `!isInitialized`

#### 4. Open Positions
- Section header: `"Open Lots (N)"`
- Each row: type badge `BUY` (blue) / `SELL` (orange), lots, open price, P&L colored
- Empty state: gray `"No open positions"`

#### 5. Manual Trade Card
- Lot size stepper: 0.01 – 1.00, step 0.01, show 2 decimal places
- Three buttons in a row:
  - `[BUY]` blue filled
  - `[SELL]` orange filled
  - `[Close All]` red outlined
- Each shows confirmation alert before executing
- Show result toast at bottom (green=success, red=error, auto-dismiss 2s)
- Haptic feedback on tap (`UIImpactFeedbackGenerator`)

#### 6. Recent Trades
- Last 20 trades
- Each row: action badge (BUY/SELL/CLOSE colored), price, lots · time (HH:mm) · reason truncated 30 chars

---

### Settings Sheet (gear icon)

**Bot Settings (auto-loaded, saved with PUT /api/settings):**
- Lot Size: Stepper 0.01 – 1.00
- Max Trades/Day: Stepper 0 – 50 (show `∞` when 0)
- Trading Start UTC: TextField `"HH:mm"` or blank
- Trading End UTC: TextField `"HH:mm"` or blank
- `[Save Settings]` button

**Account section:**
- `"My API Key"` row with copy-to-clipboard button
- `[Disconnect]` red button → clears all AppStorage → shows setup sheet

---

## GoldBotApp.swift

```swift
import SwiftUI

@main
struct GoldBotApp: App {
    var body: some Scene {
        WindowGroup {
            ContentView()
        }
    }
}
```

---

## General Requirements

- Swift Concurrency (`async/await`) throughout — no Combine or callbacks
- No third-party packages
- Light and dark mode support
- Skeleton loading state on first fetch
- Offline banner if 3 consecutive fetches fail
- All errors show a dismissable red banner at the top of the screen
