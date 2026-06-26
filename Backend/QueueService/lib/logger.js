"use strict";

function ts() {
  return new Date().toISOString();
}

function info(scope, message, extra) {
  if (extra !== undefined) {
    console.log(`[${ts()}][${scope}] ${message}`, extra);
    return;
  }
  console.log(`[${ts()}][${scope}] ${message}`);
}

function warn(scope, message, extra) {
  if (extra !== undefined) {
    console.warn(`[${ts()}][${scope}] ${message}`, extra);
    return;
  }
  console.warn(`[${ts()}][${scope}] ${message}`);
}

function error(scope, message, extra) {
  if (extra !== undefined) {
    console.error(`[${ts()}][${scope}] ${message}`, extra);
    return;
  }
  console.error(`[${ts()}][${scope}] ${message}`);
}

module.exports = { info, warn, error };
