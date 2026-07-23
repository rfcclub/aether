const ws = new WebSocket("ws://localhost:5099/ws");

ws.onopen = () => {
  console.log("Connected!");
  
  // 1. Request history
  console.log("Requesting history...");
  ws.send(JSON.stringify({
    type: "get_history",
    group: "default",
    limit: 5
  }));

  // 2. Trigger a /new command
  console.log("Triggering /new command...");
  ws.send(JSON.stringify({
    type: "command",
    text: "/new",
    group: "default"
  }));

  // 3. Send message "Hi" after 1 second to trigger LLM startup sequence
  setTimeout(() => {
    console.log("Sending user message 'Hi' to trigger LLM...");
    ws.send(JSON.stringify({
      type: "message",
      text: "Hi",
      group: "default"
    }));
  }, 1000);
};

ws.onmessage = (event) => {
  console.log("Received:", event.data);
};

ws.onerror = (err) => {
  console.error("Error:", err);
};

ws.onclose = () => {
  console.log("Closed.");
};

// Auto close after 25 seconds (since LLM processing might take some time)
setTimeout(() => {
  console.log("Closing connection...");
  ws.close();
}, 25000);
