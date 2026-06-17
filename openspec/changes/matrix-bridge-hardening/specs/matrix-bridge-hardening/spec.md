## ADDED Requirements

### Requirement: matrix-auto-join
The AppService bridge SHALL intercept room membership invite events targeting registered agent user IDs and automatically send join requests.

#### Scenario: Auto joining invited room
- **WHEN** Thoor invites `@aura:localhost` to a newly created room `#thoor-aura:localhost`
- **THEN** The bridge intercepts the event, joins the room immediately, and publishes a greeting message

### Requirement: synapse-offline-retry
The bridge service SHALL monitor connection status to the homeserver and retry connections using exponential backoff when Synapse goes offline.

#### Scenario: Synapse drops and recovers
- **WHEN** The Synapse homeserver goes offline for 30 seconds
- **THEN** `nexus-chat` stays active, keeps trying to reconnect, and restores event routing immediately when Synapse comes back online

