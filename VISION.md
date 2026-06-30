# MITMI Vision

## 0. Accepted Planning Decisions

Current accepted decisions:

- MITMI should evolve from "Modbus In The Middle" toward the broader meaning "Man In The Middle".
- Public and commercial positioning should still emphasize authorized industrial traffic mediation, diagnostics, testing, and replay rather than unauthorized interception.
- v0.1 should primarily be a diagnostic proxy.
- v0.1 should be Modbus-aware from day one, while keeping Modbus-specific logic isolated in the Modbus protocol plugin.
- NModbus is the accepted initial Modbus dependency, referenced only from the Modbus protocol project.
- Replay, fault simulation, caching, queuing, bridging, automation, and scripting remain important future directions, but they should not distract from proving that the diagnostic proxy works reliably first.

## 1. Vision

MITMI exists to help industrial automation professionals observe, understand, intercept, record, replay, and eventually transform industrial communication in a controlled and auditable way.

The original name, "Modbus In The Middle", is useful for the first implementation but too narrow for the long-term product. MITMI should evolve toward the broader meaning "Man In The Middle" and become a protocol-independent industrial communication mediation platform. The product should not be architected as a Modbus tool that later grows extra adapters. Modbus TCP is the first protocol plugin and proving ground.

MITMI should serve industrial automation engineers, system integrators, PLC developers, SCADA engineers, commissioning teams, field service engineers, and test engineers who need visibility and controlled intervention between industrial systems.

Long-term product themes:

- Observe industrial communication without changing normal system behavior.
- Intercept traffic when explicitly configured to do so.
- Log communication in a replayable and auditable form.
- Replay selected communication for testing, troubleshooting, and regression scenarios.
- Simulate failure modes safely.
- Cache responses where the protocol and use case allow it.
- Queue write operations where the risk is understood and visible.
- Bridge protocols through explicit mappings rather than hidden magic.
- Collect metrics for diagnostics and product telemetry.
- Support future automation and scripting without making scripting part of the core domain.

MITMI should be a commercial-grade tool, not a demo proxy. That means it must treat safety, traceability, diagnostics, licensing, configuration, upgrade paths, and supportability as first-class concerns.

## 2. Strategic Assumptions To Challenge

### 2.1 "Protocol-independent" does not mean "protocols are interchangeable"

Industrial protocols differ deeply:

- Modbus TCP is simple, request-response oriented, and mostly stateless at the application layer.
- MQTT is brokered pub/sub and topic-centric.
- OPC UA has sessions, subscriptions, security, complex data models, and service semantics.
- BACnet has its own object model and discovery behaviors.
- Serial protocols have timing, framing, and transport concerns that do not look like TCP.
- HTTP can be request-response, streaming, REST-like, RPC-like, or vendor-specific.

Risk: a generic core can become either too abstract to be useful or so generic that every plugin bypasses it.

Recommended stance: the core should be protocol-neutral, not protocol-naive. It should define common concepts such as endpoint, connection/session, message, transaction, timestamp, direction, metadata, payload, pipeline result, and capabilities. Protocol-specific semantics should live in plugins.

### 2.2 "Minimal changes later" means stable extension points, not zero redesign

Adding OPC UA or MQTT later will almost certainly expose design gaps. The goal is to isolate change, not eliminate it.

Recommended stance: design the first version around explicit extension contracts, test those contracts with Modbus TCP, and document where the abstraction is intentionally incomplete.

### 2.3 "Man In The Middle" is technically accurate but commercially sensitive

The phrase can imply unauthorized interception. MITMI can use "Man In The Middle" as the broader product meaning, but a commercial industrial product should emphasize authorized mediation, diagnostics, validation, and test support.

Possible future expansions of MITMI:

- Message Integration and Traffic Mediation Interface
- Middleware for Industrial Traffic Monitoring and Interception
- Managed Industrial Traffic Mediation Infrastructure

Recommendation: keep MITMI as the product name for now, but avoid positioning the product publicly as an offensive MITM tool.

### 2.4 Replay is harder than logging

Logging bytes is relatively easy. Replay requires preserving timing, transaction correlation, direction, connection state, protocol semantics, and environmental assumptions.

Risk: a replay feature that appears simple may produce misleading tests or unsafe behavior.

Recommended stance: separate traffic capture from replay scenarios. A capture is historical evidence. A replay scenario is an executable test artifact derived from one or more captures.

### 2.5 Bridging protocols is a product line, not a checkbox

Bridging Modbus to MQTT, OPC UA to HTTP, or BACnet to MQTT requires explicit data mapping, type conversion, timing policy, error handling, identity, and write semantics.

Risk: generic "bridge anything to anything" promises can produce fragile systems.

Recommended stance: treat bridging as a later capability built on a mapping layer, not as a v0.1 core feature.

## 3. Product Scope

### 3.1 Included In The First Product Direction

MITMI should eventually support:

- Passive observation.
- Active interception.
- Structured traffic logging.
- Replay workflows.
- Fault simulation.
- Response caching.
- Write queuing.
- Protocol bridging.
- Metrics collection.
- Plugin-based protocol expansion.
- JSON-based configuration.
- CLI-first operation.
- Console host for Windows and Linux.
- Clean separation of core, protocols, storage, logging, metrics, licensing, plugins, configuration, and UI.

### 3.2 Recommended v0.1 Scope

v0.1 should be intentionally narrow:

- Console application.
- Windows and Linux support.
- JSON configuration.
- Modbus TCP protocol plugin.
- Modbus-aware request/response correlation and diagnostics from day one.
- TCP listener and upstream target connection.
- Diagnostic proxy operation as the primary product experience.
- Observe/pass-through mode.
- Structured request/response logging.
- Basic capture file format.
- Basic replay from recorded Modbus TCP transactions, treated as a secondary validation feature.
- Basic metrics exposed through logs or a local metrics sink.
- Clear diagnostics for startup, configuration, connection, and protocol errors.
- Internal dependency injection.
- Static plugin registration through composition.

Recommended exclusions from v0.1:

- Dynamic third-party plugin loading.
- Web dashboard.
- Licensing enforcement beyond a placeholder entitlement boundary.
- Cross-protocol bridging.
- Scripting engine.
- Complex write queuing.
- Production-grade caching.
- OPC UA, MQTT, BACnet, HTTP, or Serial support.
- Distributed deployment.
- Cloud services.

Reason: v0.1 should prove the mediation model, plugin boundary, pipeline shape, capture model, and operational experience before expanding the feature matrix.

### 3.3 Explicit Non-Goals For The First Version

- MITMI is not a SCADA system.
- MITMI is not a historian.
- MITMI is not an HMI.
- MITMI is not a PLC programming tool.
- MITMI is not a packet sniffer like Wireshark.
- MITMI is not an intrusion tool.
- MITMI is not a generic ESB or enterprise integration platform.

These exclusions matter because the product can easily drift into adjacent markets.

## 4. Architecture Overview

### 4.1 Architectural Style

MITMI should follow clean architecture principles:

- The domain and application core should not depend on protocol implementations.
- Protocol plugins should depend inward on stable abstractions.
- Infrastructure concerns should be replaceable.
- The console UI should be a host, not the application.
- Licensing should be isolated from business rules.
- Storage should be abstracted behind explicit ports.
- Metrics should be emitted through a neutral interface.
- Configuration should be parsed at the edge and converted into validated runtime models.

Composition should be preferred over inheritance. Inheritance may still be useful for narrow framework integration, but MITMI's own extension model should favor small interfaces, capability descriptors, and composed services.

### 4.2 Proposed Layers

#### Domain Core

Owns protocol-neutral concepts:

- Endpoint identity.
- Communication direction.
- Message envelope.
- Transaction or exchange identity.
- Capture record metadata.
- Pipeline decisions.
- Replay scenario concepts.
- Failure simulation policies.
- Capability descriptors.

The core should not know what a Modbus function code, MQTT topic, OPC UA node, BACnet object, or HTTP route is.

Weakness to watch: if the domain core only understands opaque byte arrays, later features such as caching, replay, filtering, and bridging will become awkward. The core needs a neutral envelope plus plugin-provided semantic metadata.

#### Application Layer

Coordinates use cases:

- Start a mediation session.
- Load and validate configuration.
- Register protocol adapters.
- Build middleware pipelines.
- Run observe/intercept sessions.
- Record traffic.
- Build replay scenarios.
- Apply policy decisions.
- Emit metrics.
- Check entitlements where features are selected.

The application layer should own orchestration, not protocol parsing.

#### Protocol Plugin Layer

Each protocol implementation should provide:

- Protocol metadata.
- Listener or connector factories.
- Message parser/serializer.
- Optional semantic decoder.
- Capability declaration.
- Protocol-specific validation.
- Optional replay support.
- Optional caching support.
- Optional bridge mapping support.

Important design point: every plugin should declare capabilities. The core should not assume all protocols support the same features.

Examples:

- Modbus TCP may support request/response correlation and simple replay.
- MQTT may support topic observation and publish replay, but not the same transaction model.
- OPC UA may support subscriptions, sessions, security negotiation, and service calls.
- Serial protocols may support byte stream framing and timing-sensitive replay.

#### Middleware Pipeline

The pipeline should process protocol-neutral message envelopes and protocol-specific metadata.

Potential middleware categories:

- Observability middleware.
- Logging middleware.
- Filtering middleware.
- Fault injection middleware.
- Cache middleware.
- Queue middleware.
- Transform middleware.
- Metrics middleware.
- Authorization/licensing middleware at feature boundaries.

Pipeline design trade-off:

- A simple linear pipeline is easy to reason about and fits Modbus TCP.
- More complex protocols may require event-driven or stateful pipelines.

Recommendation: start with a linear async pipeline for v0.1, but model pipeline context carefully enough to support stateful sessions later.

#### Storage Layer

Storage should support:

- Append-only capture records.
- Configuration snapshots attached to captures.
- Replay scenario artifacts.
- Indexes for filtering and diagnostics.
- Retention policies later.

Trade-off:

- JSON logs are human-friendly but inefficient for binary traffic and high-volume capture.
- Binary formats are efficient but harder to inspect and support.

Recommendation: use a structured capture model conceptually, then choose an implementation later. For early planning, separate "traffic log for humans" from "capture store for replay".

#### Configuration Layer

Configuration should be JSON-based but validated into strongly typed runtime models.

Configuration should describe:

- Protocol instances.
- Listeners.
- Upstream endpoints.
- Routes.
- Pipelines.
- Logging sinks.
- Capture settings.
- Replay settings.
- Metrics settings.
- Plugin settings.
- Feature selections.

Risk: a single giant JSON file will become hard to maintain.

Alternative approaches:

- Single file for v0.1.
- Multiple included files later.
- Environment variable overrides for deployment.
- Profiles for field usage.

Recommendation: start with one JSON configuration file, but design configuration sections as if they can be split later.

#### Licensing Layer

Licensing should expose entitlement decisions through a small interface.

The rest of the product should ask questions such as:

- Is this feature available?
- What limit applies?
- What edition is active?
- Is offline use allowed?

The rest of the product should not contain Free/Professional conditionals.

Recommendation: use feature capability checks at application boundaries. Do not let protocol plugins know about editions.

#### Metrics Layer

Metrics should be emitted as neutral events/counters:

- Connections accepted.
- Connections failed.
- Upstream connection latency.
- Messages observed.
- Messages modified.
- Messages blocked.
- Replay operations.
- Cache hits/misses.
- Queue depth.
- Protocol parse errors.
- Middleware latency.

Recommendation: do not tie the core to a dashboard. A future dashboard should consume metrics; it should not define the metrics model.

#### User Interface Layer

v0.1 should be CLI-first and console-hosted.

The UI should:

- Start sessions.
- Validate configuration.
- Show effective configuration.
- Show health/status.
- Run replay scenarios.
- Export diagnostics.

The UI should not own business workflows. A future web dashboard should use the same application services.

### 4.3 Conceptual Runtime Flow

1. Host starts.
2. Configuration is loaded and validated.
3. Plugins are discovered or registered.
4. Protocol instances are created.
5. Middleware pipelines are built.
6. Listeners/connectors start.
7. Messages are converted into envelopes.
8. Pipeline processes each envelope.
9. Decisions are applied: pass, block, modify, delay, replay, cache, queue, or fail.
10. Logs, captures, and metrics are emitted.
11. Session shutdown flushes storage and diagnostics.

## 5. Key Design Principles

### 5.1 Core Has No Protocol Knowledge

The core must not contain Modbus, BACnet, MQTT, OPC UA, HTTP, or Serial-specific logic.

Challenge: the core still needs meaningful abstractions. "No protocol knowledge" should not degrade into "everything is bytes".

### 5.2 Protocols Are Plugins

Protocols should be added through plugin-style modules.

Recommendation: use static in-process plugins first. Defer dynamic plugin loading until the internal contracts are stable.

Reason: dynamic plugin loading introduces versioning, security, dependency isolation, compatibility, signing, and support burden.

### 5.3 Capabilities Over Assumptions

The system should ask a plugin what it supports.

Examples:

- Supports passive observation.
- Supports active interception.
- Supports replay.
- Supports semantic decoding.
- Supports response caching.
- Supports write queuing.
- Supports bridge mappings.
- Supports secure transport.

### 5.4 Configuration Over Code

Users should configure behavior through JSON rather than custom code for normal use cases.

Challenge: configuration can become a programming language by accident.

Recommendation: keep configuration declarative. Introduce scripting later only for scenarios where declarative policy is insufficient.

### 5.5 CLI First, UI Later

The console application should be a complete operational surface for v0.1.

A future web dashboard should not reshape the core. It should sit on top of the same application services.

### 5.6 Offline-First Licensing

Industrial environments are often offline, firewalled, or site-restricted.

Licensing should support offline activation and local validation eventually.

Challenge: licensing should protect the product without making field work fragile.

### 5.7 Observability Is A Product Feature

Logs, metrics, traces, and diagnostics are not afterthoughts. Field users need explainable behavior.

Every important decision should be diagnosable:

- Why was a message blocked?
- Why was a response replayed?
- Why was a cached value used?
- Why did upstream reconnect?
- Which configuration rule applied?

### 5.8 Safety Before Cleverness

Features such as write queuing, replay, fault injection, and bridging can affect industrial processes.

Recommendation: make active intervention explicit, visible, logged, and reversible where possible.

### 5.9 Maintainability Over Raw Performance

Performance matters, especially for high-volume traffic, but the early architecture should optimize for correctness, testability, and clear boundaries.

Performance-sensitive paths can be optimized after the abstractions are proven.

## 6. Roadmap

### v0.1 - Modbus TCP Diagnostic Proxy Foundation

Primary goal: prove the core architecture with one protocol by delivering a reliable diagnostic proxy first.

Candidate features:

- Console host.
- JSON configuration.
- Modbus TCP plugin.
- Diagnostic proxy workflow.
- Passive observe/pass-through mode.
- Structured logging.
- Basic capture store.
- Basic replay for selected captured Modbus TCP exchanges as a secondary feature.
- Basic metrics.
- Dependency injection.
- Static plugin registration.
- Clear diagnostics and configuration validation.

Success criteria:

- A user can place MITMI between a Modbus TCP client and server.
- MITMI can observe traffic without changing behavior.
- MITMI can log request/response exchanges with timestamps and metadata.
- MITMI can replay a simple recorded scenario in a controlled test setup.
- The core does not contain Modbus-specific logic.

### v0.2 - Policy And Fault Simulation

Primary goal: make interception useful and controlled.

Candidate features:

- Rule-based filtering.
- Delay injection.
- Drop connection simulation.
- Timeout simulation.
- Response override for selected Modbus operations.
- Safer replay scenario editing.
- Better capture indexing.
- More metrics.
- Config profiles.
- Early entitlement boundary for Free/Professional features.

Success criteria:

- A user can simulate common communication failures.
- A user can understand exactly which rule affected a message.
- Fault simulation does not require code changes.

### v0.3 - Caching And Queuing Experiments

Primary goal: test advanced mediation features without over-generalizing.

Candidate features:

- Response cache policies for safe Modbus reads.
- Write queue policies for controlled test environments.
- Queue visibility and manual drain/discard controls.
- Capture-to-replay tooling improvements.
- Storage format hardening.
- Plugin capability model refinement.

Success criteria:

- Caching and queuing are explicit, visible, and limited by protocol capabilities.
- Dangerous operations have clear safeguards and logs.

### v0.4 - Second Protocol Plugin

Primary goal: validate protocol independence.

Candidate protocols:

- MQTT if the goal is pub/sub and cloud/edge integration.
- HTTP if the goal is simple adapter diversity.
- Serial/RTU if the goal is industrial field relevance.
- BACnet if building automation is a target market.
- OPC UA if enterprise industrial integration is a priority.

Recommendation: choose the second protocol based on which assumption needs testing most. MQTT challenges the request/response model. Serial challenges transport abstraction. OPC UA challenges session/security complexity.

Success criteria:

- The second protocol does not require rewriting the core.
- Any required core changes are general and documented.
- The capability model becomes clearer rather than more complicated.

### v1.0 - Commercial Foundation

Primary goal: deliver a commercially credible product.

Candidate features:

- Stable plugin contracts.
- Free and Professional editions.
- Offline-first licensing.
- Installer/package distribution for Windows and Linux.
- Robust diagnostics bundle export.
- Capture/replay workflow suitable for field use.
- Security posture documentation.
- Hardening for long-running sessions.
- Documentation for integrators.
- Compatibility test matrix.
- Support lifecycle policy.

Success criteria:

- The product can be installed, configured, diagnosed, licensed, and supported by real customers.
- The architecture can accept additional protocols without product-wide rewrites.

### Post-v1.0 - Platform Expansion

Candidate directions:

- Dynamic plugin loading with signing and compatibility checks.
- Web dashboard.
- Protocol bridging.
- Scripting/automation engine.
- Advanced replay lab.
- Fleet management.
- Remote diagnostics.
- Capture anonymization/redaction.
- Cloud optional services.
- Marketplace or partner protocol plugins.

## 7. Major Architectural Decision Points

These decisions should be made deliberately before implementation.

### 7.1 Message Model

Question: should the core message envelope carry only bytes and metadata, or should it support optional semantic fields?

Recommendation: use an envelope with raw payload, protocol identity, direction, timestamps, correlation identifiers, and optional semantic metadata supplied by plugins.

Trade-off:

- Raw-only is simple but limits filtering, caching, replay, and bridging.
- Semantic metadata enables features but risks leaking protocol concepts into the core.

### 7.2 Pipeline Model

Question: should middleware operate on individual messages, request/response exchanges, sessions, or streams?

Recommendation: support message-level processing first, with correlation into exchanges where the protocol plugin can provide it.

Trade-off:

- Message-level is simple and fast.
- Exchange-level is more useful for Modbus and replay.
- Stream/session-level becomes necessary for more complex protocols.

### 7.3 Plugin Loading Strategy

Question: should v0.1 support dynamic plugin loading?

Recommendation: no. Use statically registered plugins in v0.1, while designing contracts with dynamic loading in mind.

Trade-off:

- Static plugins are simpler to build, test, and support.
- Dynamic plugins are commercially attractive but introduce compatibility, security, and dependency problems.

### 7.4 Capture Format

Question: should captures be optimized for human inspection, replay fidelity, or performance?

Recommendation: separate human-readable logs from replay-oriented capture storage.

Trade-off:

- One format is simpler.
- Two outputs avoid compromising replay fidelity for readability.

### 7.5 Licensing Boundary

Question: where should edition enforcement happen?

Recommendation: at application feature boundaries and plugin capability activation points, never inside domain logic.

Trade-off:

- Centralized checks are cleaner and easier to audit.
- Some feature limits may need runtime counters or resource controls.

### 7.6 Configuration Shape

Question: should configuration model protocols, routes, pipelines, or sessions as the top-level concept?

Recommendation: make "session" or "route" the operational unit, with protocol instances and pipelines attached.

Trade-off:

- Protocol-first config is intuitive for developers.
- Route/session-first config is more intuitive for users operating mediation scenarios.

## 8. Initial Architecture Risks

### Risk: Over-generalizing From Modbus

Mitigation: explicitly document which abstractions are Modbus-proven and which are speculative. Add a second protocol before claiming the plugin model is stable.

### Risk: Unsafe Active Intervention

Mitigation: make intercept, replay, cache, queue, and fault simulation opt-in, visible, logged, and clearly named.

### Risk: Replay Produces False Confidence

Mitigation: attach environment metadata, timing, configuration snapshot, and limitations to replay scenarios.

### Risk: Licensing Pollutes Architecture

Mitigation: isolate entitlements and feature gates. Avoid edition checks in protocol plugins and domain objects.

### Risk: Dynamic Plugins Too Early

Mitigation: start with compile-time modules. Introduce dynamic loading after contracts and compatibility rules are mature.

### Risk: Configuration Becomes Too Complex

Mitigation: validate configuration aggressively, provide effective-configuration output, and support examples/profiles later.

### Risk: Logs Leak Sensitive Industrial Data

Mitigation: plan for redaction, capture encryption, retention policies, and diagnostics bundle controls before commercial release.

### Risk: Web Dashboard Influences Core Too Early

Mitigation: keep the core host-neutral. Treat CLI and future web UI as adapters.

## 9. Open Questions

These should be answered before implementation begins:

1. Is MITMI primarily a field troubleshooting tool, a test lab tool, an integration gateway, or a long-running production mediator?
2. Should v0.1 prioritize passive observability or active interception?
3. What is the expected traffic volume for the first customer scenarios?
4. Should v0.1 capture raw binary payloads, decoded protocol fields, or both?
5. How exact must replay timing be in the first version?
6. Should replay target the original server/client topology or a simulated endpoint?
7. What level of configuration usability is expected for field engineers?
8. Will MITMI usually run on an engineer laptop, an industrial PC, a server, or an edge gateway?
9. Are there regulatory, safety, or customer IT constraints around storing traffic captures?
10. Should Modbus RTU/Serial be the second protocol because of industrial relevance, or should MQTT/HTTP be second to validate architectural diversity?
11. What limitations are acceptable in the Free edition?
12. Should commercial licensing be planned before v1.0, or only after the core workflow is proven?

## 10. Recommended Next Planning Steps

### Step 1 - Define Product Mode

MITMI v0.1 is mainly:

- A diagnostic proxy.

Deferred product modes:

- A replay/test tool.
- A failure simulation tool.
- A future integration gateway.

Rationale: starting as a diagnostic proxy is narrower, easier to validate, and still supports the long-term roadmap.

### Step 2 - Define The Operational Unit

Choose the main configuration concept:

- Session.
- Route.
- Pipeline.
- Protocol instance.

Recommendation: use "session" for the user-facing concept and "route" internally if needed.

### Step 3 - Define The Message Envelope

Before implementation, define what every protocol plugin must provide to the core.

Recommendation: include raw payload, direction, timestamps, endpoint metadata, correlation ID if available, protocol ID, and optional semantic metadata.

### Step 4 - Define The v0.1 Modbus Scenario

Pick one concrete scenario:

- Modbus TCP client connects to MITMI.
- MITMI connects to upstream Modbus TCP server.
- MITMI observes, logs, passes through, and can replay selected exchanges later.

Recommendation: resist adding multi-client, multi-server, bridging, and dynamic plugin loading until this works cleanly.

### Step 5 - Define Capture And Replay Guarantees

Be explicit:

- What is guaranteed?
- What is best effort?
- What is unsupported?

Recommendation: v0.1 replay should be described as controlled test replay, not a perfect reconstruction of a live industrial network.

### Step 6 - Define Commercial Boundaries Early

Decide where Free vs Professional will differ, without implementing licensing hacks.

Potential Free limitations:

- Number of concurrent sessions.
- Capture duration.
- Replay scenario count.
- Advanced fault simulation.
- Advanced export.
- Advanced protocol plugins.

Recommendation: limit by capabilities and quotas at application boundaries, not by scattering edition checks.

## 11. Current Working Position

The recommended initial position is:

MITMI v0.1 should be a .NET console-based, protocol-neutral mediation host with a statically registered Modbus TCP plugin. It should support observe/pass-through operation, structured logging, basic capture, basic replay, metrics, and strong configuration validation. The architecture should prepare for plugins, licensing, storage options, and future UI surfaces, but should not implement dynamic plugin loading, dashboards, scripting, or bridging in the first version.

This position is intentionally conservative. It protects the architecture from becoming a Modbus-specific tool while avoiding premature platform complexity.

The accepted v0.1 product mode is diagnostic proxy first. Once the diagnostic proxy is running reliably, the next planning decision should be whether to deepen replay, add fault simulation, or start validating the plugin model with a second protocol.
