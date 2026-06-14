use crate::events::{AppEvent, Message, ModelsPayload, ProviderGroup, Role};
use futures_util::{SinkExt, StreamExt};
use tokio::sync::mpsc;
use tokio_tungstenite::{connect_async, tungstenite::Message as WsMessage};

pub async fn ws_task(url: String, group: String, tx: mpsc::Sender<AppEvent>, mut rx: mpsc::Receiver<String>) {
    let mut attempt = 0u32;
    loop {
        match connect_async(&url).await {
            Ok((ws_stream, _)) => {
                attempt = 0;
                let _ = tx.send(AppEvent::Connected).await;
                let (mut write, mut read) = ws_stream.split();

                // Send list_models immediately after connect
                let _ = write.send(WsMessage::Text(
                    r#"{"type":"list_models"}"#.to_string().into()
                )).await;

                // Send get_history immediately after connect
                let history_req = serde_json::json!({
                    "type": "get_history",
                    "group": group,
                    "limit": 50
                });
                let _ = write.send(WsMessage::Text(history_req.to_string().into())).await;

                // 2-second timeout: if history not received, send empty HistoryLoaded
                let history_tx = tx.clone();
                tokio::spawn(async move {
                    tokio::time::sleep(tokio::time::Duration::from_secs(2)).await;
                    let _ = history_tx.send(AppEvent::HistoryLoaded(vec![])).await;
                });

                loop {
                    tokio::select! {
                        // Outbound: messages from app to send
                        msg = rx.recv() => {
                            match msg {
                                Some(json) => {
                                    if write.send(WsMessage::Text(json.into())).await.is_err() {
                                        break;
                                    }
                                }
                                None => return, // channel closed = quit
                            }
                        }
                        // Inbound: messages from server
                        item = read.next() => {
                            match item {
                                Some(Ok(WsMessage::Text(text))) => {
                                    handle_inbound(text.as_str(), &tx).await;
                                }
                                Some(Ok(WsMessage::Close(_))) | None => break,
                                Some(Err(_)) => break,
                                _ => {}
                            }
                        }
                    }
                }
                let _ = tx.send(AppEvent::Disconnected("connection closed".into())).await;
            }
            Err(e) => {
                let _ = tx.send(AppEvent::Disconnected(e.to_string())).await;
            }
        }

        // Exponential backoff: 1, 2, 4, 8, … capped at 30 seconds
        let wait = std::cmp::min(1u64 << attempt, 30);
        attempt = attempt.saturating_add(1);
        tokio::time::sleep(tokio::time::Duration::from_secs(wait)).await;
    }
}

async fn handle_inbound(json: &str, tx: &mpsc::Sender<AppEvent>) {
    let Ok(val) = serde_json::from_str::<serde_json::Value>(json) else { return };
    let msg_type = val["type"].as_str().unwrap_or("");
    match msg_type {
        "chunk" | "streaming_chunk" => {
            if let Some(text) = val["text"].as_str() {
                let _ = tx.send(AppEvent::StreamChunk(text.to_string())).await;
            }
        }
        "message" | "complete" => {
            if let Some(text) = val["text"].as_str() {
                let _ = tx.send(AppEvent::MessageComplete(text.to_string())).await;
            }
        }
        "typing" => {
            let is_typing = val["is_typing"].as_bool()
                .unwrap_or_else(|| val["status"].as_str() == Some("typing"));
            let _ = tx.send(AppEvent::Typing(is_typing)).await;
        }
        "error" => {
            let msg = val["message"].as_str()
                .or_else(|| val["text"].as_str())
                .unwrap_or("unknown error");
            let _ = tx.send(AppEvent::BackendError(msg.to_string())).await;
        }
        "models" => {
            let payload = parse_models_payload(&val);
            let _ = tx.send(AppEvent::ModelsLoaded(Some(payload))).await;
        }
        "git_status_response" => {
            let mut files = Vec::new();
            if let Some(arr) = val["files"].as_array() {
                for item in arr {
                    let path = item["path"].as_str().unwrap_or("").to_string();
                    let status = item["status"].as_str().unwrap_or("Unstaged").to_string();
                    files.push((path, status));
                }
            }
            let _ = tx.send(AppEvent::GitStatusLoaded(files)).await;
        }
        "git_diff_response" => {
            if let Some(diff) = val["diff"].as_str() {
                let _ = tx.send(AppEvent::GitDiffLoaded(diff.to_string())).await;
            }
        }
        "history" => {
            let messages = val["messages"].as_array().unwrap_or(&vec![]).iter().map(|m| {
                let role = match m["role"].as_str().unwrap_or("user") {
                    "assistant" => Role::Assistant,
                    _ => Role::User,
                };
                let content = m["content"].as_str().unwrap_or("").to_string();
                let timestamp = m["timestamp"].as_str()
                    .and_then(|s| chrono::DateTime::parse_from_rfc3339(s).ok())
                    .map(|dt| dt.with_timezone(&chrono::Utc))
                    .unwrap_or_else(chrono::Utc::now);
                Message { role, content, timestamp, is_historical: true }
            }).collect();
            let _ = tx.send(AppEvent::HistoryLoaded(messages)).await;
        }
        _ => {} // unknown types silently ignored
    }
}

fn parse_models_payload(val: &serde_json::Value) -> ModelsPayload {
    let current = val["current"].as_str().unwrap_or("none").to_string();
    let think_effort = val["think_effort"].as_str().map(str::to_string);
    let providers = val["providers"].as_array().unwrap_or(&vec![]).iter().map(|p| {
        ProviderGroup {
            name: p["name"].as_str().unwrap_or("?").to_string(),
            models: p["models"].as_array().unwrap_or(&vec![]).iter()
                .filter_map(|m| m.as_str().map(str::to_string))
                .collect(),
        }
    }).collect();
    ModelsPayload { current, think_effort, providers }
}
