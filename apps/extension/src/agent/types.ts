import type { components } from "@overhaul/api-client";

type Schemas = components["schemas"];

export type HeartbeatRequest = Schemas["AgentHeartbeatRequest"];
export type Heartbeat = Schemas["AgentHeartbeatDto"];
export type MarketplaceReport = Schemas["AgentMarketplaceReport"];
export type PlatformPolicy = Schemas["AgentPlatformPolicyDto"];
export type AgentAccount = Schemas["AgentAccountDto"];
export type Work = Schemas["AgentWorkDto"];
export type AgentRun = Schemas["AgentRunDto"];
export type RunIssue = Schemas["AgentRunIssueInput"];
export type IngestOrder = Schemas["IngestPlatformOrderInput"];
export type IngestOrderLine = Schemas["IngestPlatformOrderLineInput"];
export type IngestListing = Schemas["IngestPlatformListingInput"];
export type FulfillmentStage = Schemas["PlatformFulfillmentStage"];
export type SyncRun = Schemas["SyncRunDto"];
export type LoopKind = Schemas["SyncLoopKind"];
export type Trigger = Schemas["SyncTrigger"];
export type FailureKind = Schemas["SyncFailureKind"];
export type Pairing = Schemas["PairAgentResultDto"];
