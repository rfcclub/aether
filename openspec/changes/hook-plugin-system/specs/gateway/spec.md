## ADDED Requirements

### Requirement: OnMessageReceived Hook Fired on Inbound Messages

`ChannelMessageProcessor.HandleMessageAsync` SHALL fire `HookPoint.OnMessageReceived` when a message arrives from any channel, BEFORE the access control check. The hook SHALL receive an `OnMessageReceivedContext` with `ChatId`, `SenderId`, `ChannelName`, and `Text`. The hook SHALL be able to drop the message (`Dropped = true`) or transform its text (`OverrideText`).

#### Scenario: Hook drops spam message
- **WHEN** an OnMessageReceived hook sets `Dropped = true`
- **THEN** the message SHALL NOT be routed or processed
- **AND** no response SHALL be sent

#### Scenario: Hook transforms message text
- **WHEN** an OnMessageReceived hook sets `OverrideText = "transformed text"`
- **THEN** all subsequent processing (access check, routing, LLM call) SHALL use "transformed text"

### Requirement: OnMessageRouted Hook Fired After Agent Resolution

`ChannelMessageProcessor` SHALL fire `HookPoint.OnMessageRouted` after `MessageRouter.RouteAsync` resolves the target agent. The hook SHALL be able to reroute the message to a different agent via `RerouteToAgent = true` and `RerouteAgentName`.

#### Scenario: Hook reroutes to different agent
- **WHEN** an OnMessageRouted hook sets `RerouteToAgent = true` and `RerouteAgentName = "debug-agent"`
- **THEN** the message SHALL be processed by "debug-agent" instead of the originally routed agent

### Requirement: OnMessageSent Hook Fired Before Channel Delivery

`ChannelMessageProcessor` SHALL fire `HookPoint.OnMessageSent` before calling `IChannel.SendMessageAsync`, using `HookEngine.RunAllAsync`. The hook SHALL be able to transform the text (`OverrideText`) or suppress delivery entirely (`Suppress = true`).

#### Scenario: Hook suppresses response
- **WHEN** an OnMessageSent hook sets `Suppress = true`
- **THEN** no message SHALL be sent to the channel

#### Scenario: Hook transforms output
- **WHEN** an OnMessageSent hook sets `OverrideText = "formatted response"`
- **THEN** "formatted response" SHALL be sent to the channel instead of the original text
