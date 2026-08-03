// ────────────────────────────────────────────────────────────────────────────
// Auth models — coinciden con los DTOs del AuthController del backend
// ────────────────────────────────────────────────────────────────────────────

/** POST /api/auth/login */
export interface LoginRequest {
  username: string;
  password: string;
}

/** Respuesta de /api/auth/login, /api/auth/confirmarEmail y /api/auth/google */
export interface LoginResponse {
  token: string;
  username: string;
  role: string;
}

/** PUT /api/auth/crearUser */
export interface CreateUserRequest {
  username: string;
  passwordPlain: string;
  email: string;
  fullName?: string;
}

/** PUT /api/auth/cambiarPassword */
export interface ChangePasswordRequest {
  username: string;
  oldPassword: string;
  newPassword: string;
  newPasswordRep: string;
}

/** POST /api/auth/enviarReset — solo necesita el email */
export interface SendPasswordResetRequest {
  email: string;
}

/** POST /api/auth/resetPassword */
export interface ResetPasswordByTokenRequest {
  token: string;
  newPassword: string;
}

/** POST /api/auth/google */
export interface GoogleLoginRequest {
  idToken: string;
}
