# RimSynapse-DevTools Design Document

## Overview
RimSynapse-DevTools provides a suite of quality-of-life tools, dashboards, and debugging utilities intended for mod developers building on the RimSynapse framework. It is not required for standard end-users.

**Dependencies:** RimSynapse Core.

## Core Features

### 1. Status Dashboard
*   **Connectivity & Models:** Displays current LM Studio connectivity status and the loaded model.
*   **Hardware Stats:** Shows basic GPU utilization or memory stats if exposed by the server.
*   **Active Tracking:** Monitors the number of active pawns currently engaging with the LLM.
*   **Queue State:** Visualizes the depth and processing state of the Core async task queue.

### 2. Scalability Metrics & Capacity Planning
*   **Token Usage Tracking:** Tracks average prompt tokens, completion tokens, and generation duration over the last 50 requests.
*   **Capacity Estimation:** Estimates the maximum number of concurrent pawns the system can handle based on the active model's context window.
    *   `tokensPerPawn = max(100, avgPromptTokens / activePawnCount)` (or a configured default).
    *   `maxPawnsByContext = contextTarget / tokensPerPawn`.
*   **Throughput:** Tracks requests per minute to monitor load.

### 3. Debug Logging
*   **Structured Logs:** Maintains a structured in-memory buffer (e.g., last 100 entries) of system events, API errors, and state changes.
*   **Token Logs:** Specific logging for token counts per request to help developers optimize their prompt templates.
*   **Pawn TTL Tracker:** Monitors the Time-To-Live (TTL) of active pawn tracking (e.g., pruning after 10 minutes of inactivity).
