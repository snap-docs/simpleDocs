export class AuthError extends Error {
  constructor(status, message, code = 'auth_error') {
    super(message);
    this.name = 'AuthError';
    this.status = status;
    this.code = code;
  }
}
