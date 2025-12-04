// Send log message to Rhino console
async function logToRhino(message) {
  try {
    await fetch('http://localhost:8080/log', {
      method: 'POST',
      body: message
    });
  } catch (error) {
    // Silently fail
  }
}

export { logToRhino };