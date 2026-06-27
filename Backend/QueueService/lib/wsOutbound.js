"use strict";

const SEND_PRIORITY = {
  LOW: 0,
  NORMAL: 1,
  CRITICAL: 2,
};

class WsOutboundGate {
  constructor(options) {
    this.softLimitBytes = Math.max(65536, Number(options.softLimitBytes) || 524288);
    this.hardLimitBytes = Math.max(this.softLimitBytes, Number(options.hardLimitBytes) || 1048576);
    this.droppedSnapshots = 0;
    this.droppedMatchStates = 0;
    this.droppedLowPriority = 0;
    this.sentBytes = 0;
  }

  canSend(socket, priority) {
    if (!socket || socket.readyState !== 1) {
      return false;
    }

    const buffered = Number(socket.bufferedAmount) || 0;
    if (buffered >= this.hardLimitBytes) {
      return priority >= SEND_PRIORITY.CRITICAL;
    }
    if (buffered >= this.softLimitBytes) {
      return priority >= SEND_PRIORITY.NORMAL;
    }
    return true;
  }

  send(socket, payload, priority, onDrop) {
    if (!this.canSend(socket, priority)) {
      if (typeof onDrop === "function") {
        onDrop();
      }
      return false;
    }

    socket.send(payload);
    this.sentBytes += Buffer.isBuffer(payload) ? payload.length : Buffer.byteLength(String(payload));
    return true;
  }

  snapshot() {
    return {
      droppedSnapshots: this.droppedSnapshots,
      droppedMatchStates: this.droppedMatchStates,
      droppedLowPriority: this.droppedLowPriority,
      sentBytes: this.sentBytes,
      softLimitBytes: this.softLimitBytes,
      hardLimitBytes: this.hardLimitBytes,
    };
  }
}

module.exports = {
  SEND_PRIORITY,
  WsOutboundGate,
};
