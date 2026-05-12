## 1. Core Data Model

- [ ] 1.1 Create `src/Aether/Ui/UiDocument.cs` — `UiDocument`, `UiSection`, `UiItem` records, `UiLayout` enum
- [ ] 1.2 Create `src/Aether/Ui/UiCallback.cs` — `UiCallback` record

## 2. Core Interfaces

- [ ] 2.1 Create `src/Aether/Ui/IUiRenderer.cs` — `IUiRenderer` interface (`Render`, `SupportsInteractivity`, `SupportedLayouts`)
- [ ] 2.2 Create `src/Aether/Ui/IUiCallbackHandler.cs` — `IUiCallbackHandler` interface (`Namespace`, `HandleAsync`)

## 3. Callback Infrastructure

- [ ] 3.1 Create `src/Aether/Ui/CallbackRouter.cs` — `CallbackRouter` class that dispatches `UiCallback` to registered handlers by namespace, with try/catch per handler

## 4. Telegram Renderer

- [ ] 4.1 Create `src/Aether/Ui/Renderers/TelegramUiRenderer.cs` — converts `UiDocument` → `(string text, InlineKeyboardMarkup markup)`, maps sections to button rows, renders `Selected` items with `✅`, renders pagination row, handles `Paged` layout

## 5. WebSocket + TUI Renderers (stubs)

- [ ] 5.1 Create `src/Aether/Ui/Renderers/WebSocketUiRenderer.cs` — converts `UiDocument` → JSON with `type: "interactive"`, `sections`, `actions` fields
- [ ] 5.2 Create `src/Aether/Ui/Renderers/TuiUiRenderer.cs` — stub returning placeholder string

## 6. Model Selection Handler

- [ ] 6.1 Create `src/Aether/Ui/Handlers/ModelSelectionHandler.cs` — `IUiCallbackHandler` with namespace `"model"`, implements `browse` (provider list), `list:{provider}` (paginated model list, 8 per page), `select:{provider/model}` (validate, switch, persist), `reset` (clear override). Depends on `ProviderRouter` and `ConfigLoader` resolved via `IServiceProvider`

## 7. Channel Interface Changes

- [ ] 7.1 Modify `src/Aether/Channels/IChannel.cs` — add `OnUiCallback` event (`Func<UiCallback, Task<UiDocument?>>?`), `SendInteractiveAsync(UiDocument)` returning `Task<string?>`, `EditInteractiveAsync(string messageId, UiDocument)` returning `Task`. All with default implementations throwing `NotSupportedException`

## 8. TelegramChannel Interactive Support

- [ ] 8.1 Modify `src/Aether/Channels/TelegramChannel.cs` — parse incoming `callback_query` events, fire `OnUiCallback`, implement `SendInteractiveAsync` (render + send with inline keyboard), implement `EditInteractiveAsync` (edit text + reply markup, fallback to new message on 404)

## 9. Remaining Channel Implementations

- [ ] 9.1 Modify `src/Aether/Channels/WebSocketChannel.cs` — implement `SendInteractiveAsync` (JSON), implement `EditInteractiveAsync` (send new JSON message as replacement), bridge WebSocket action messages to `OnUiCallback`
- [ ] 9.2 Modify `src/Aether/Channels/NoOpChannel.cs` — stub `SendInteractiveAsync` returning null

## 10. ChannelMessageProcessor Wiring

- [ ] 10.1 Modify `src/Aether/Channels/ChannelMessageProcessor.cs` — subscribe to `OnUiCallback`, resolve `CallbackRouter` from DI, route callbacks, pass resulting `UiDocument` to `EditInteractiveAsync`, resolve per-agent `agentId` for handler context

## 11. Slash Command Handler

- [ ] 11.1 Modify `src/Aether/Channels/SlashCommandHandler.cs` — refactor `/model` and `/models` to return `UiDocument?` instead of plain text. `/model` with no args → delegates to `ModelSelectionHandler.HandleAsync` with `browse` action. `/model <provider/model>` → delegates to `select` action. `/models` → delegates to `browse` action.

## 12. DI Registration

- [ ] 12.1 Modify `src/Aether/Program.cs` — register `CallbackRouter` as singleton, `IUiRenderer` implementations as singletons, `IUiCallbackHandler` implementations (ModelSelectionHandler) as singletons, wire `IUiRenderer` selection by channel type

## 13. Tests

- [ ] 13.1 Create `tests/Aether.Tests/UiDocumentTests.cs` — verify data model equality, immutability
- [ ] 13.2 Create `tests/Aether.Tests/TelegramUiRendererTests.cs` — verify UiDocument → InlineKeyboardMarkup conversion: sections map to rows, Selected items get ✅, pagination row rendered correctly, Paged layout splits items
- [ ] 13.3 Create `tests/Aether.Tests/CallbackRouterTests.cs` — verify dispatch to correct handler by namespace, unknown namespace returns null, handler exception doesn't crash router
- [ ] 13.4 Create `tests/Aether.Tests/ModelSelectionHandlerTests.cs` — verify browse returns provider list, list returns paginated models, select switches model and persists, reset clears override, tapping current model returns null, invalid model returns error
- [ ] 13.5 Modify `tests/Aether.Tests/SlashCommandHandlerTests.cs` — verify `/model` returns UiDocument with provider sections, `/model fireworks/kimi` triggers select action, text fallback still works

## 14. Build Verification

- [ ] 14.1 Run `dotnet build` to verify all new code compiles
- [ ] 14.2 Run `dotnet test` to verify all tests pass
