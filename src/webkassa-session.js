class WebkassaSession {
  constructor(options) {
    if (!options || !options.client) throw new Error('client is required');
    if (!options.credentialsProvider) throw new Error('credentialsProvider is required');
    this.client = options.client;
    this.credentialsProvider = options.credentialsProvider;
    this.token = null;
  }

  async getToken() {
    if (this.token) return this.token;
    const credentials = await this.credentialsProvider();
    this.token = await this.client.authorize(credentials);
    return this.token;
  }

  invalidate() {
    this.token = null;
  }
}

function isAuthorizationError(error) {
  const message = String(error && error.message || error || '').toLowerCase();
  return (
    message.includes('unauthorized') ||
    message.includes('session expired') ||
    message.includes('token') ||
    message.includes('токен') ||
    message.includes('авторизац') ||
    message.includes('срок действия сессии истек')
  );
}

function isRecoverableWriteError(error) {
  const message = String(error && error.message || error || '').toLowerCase();
  return (
    message.includes('timeout') ||
    message.includes('network') ||
    message.includes('econnreset') ||
    message.includes('econnrefused') ||
    message.includes('enotfound') ||
    message.includes('etimedout') ||
    message.includes('eai_again') ||
    message.includes('socket') ||
    message.includes('fetch failed') ||
    message.includes('lost response')
  );
}

module.exports = {
  WebkassaSession,
  isAuthorizationError,
  isRecoverableWriteError,
};
