## 1. OpenAI-Compatible Provider Base

- [x] 1.1 Create `OpenAiCompatibleProviderBase.cs` abstract class
- [x] 1.2 Implement common HTTP client setup (auth, base URL)
- [x] 1.3 Implement `CompleteAsync` with standard OpenAI body format
- [x] 1.4 Implement response parsing (content + tool calls)
- [x] 1.5 Add `MapMessage` virtual method for custom message formats
- [x] 1.6 Implement `HealthCheckAsync` with `/models` endpoint

## 2. Anthropic-Compatible Provider Base

- [x] 2.1 Create `AnthropicCompatibleProviderBase.cs` abstract class
- [x] 2.2 Implement Anthropic-specific headers (x-api-key, anthropic-version)
- [x] 2.3 Implement `CompleteAsync` with Anthropic body format
- [x] 2.4 Implement tool use conversion to Anthropic format
- [x] 2.5 Implement token usage extraction from response headers
- [x] 2.6 Implement `HealthCheckAsync` with ping endpoint

## 3. Generic HTTP Provider

- [x] 3.1 Create `GenericHttpProvider.cs` implementing `ILLMProvider`
- [x] 3.2 Add configurable endpoint, auth header, base URL
- [x] 3.3 Implement generic JSON request/response handling
- [x] 3.4 Support multiple response formats (OpenAI style, simple content)

## 4. Refactor Existing Providers

- [x] 4.1 Refactor `FireworksProvider` to inherit from `OpenAiCompatibleProviderBase`
- [x] 4.2 Refactor `AnthropicProvider` to inherit from `AnthropicCompatibleProviderBase`
- [x] 4.3 Verify all functionality unchanged after refactor