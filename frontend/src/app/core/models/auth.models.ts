export interface User {
  userId: string;
  personId: string;
  fullName: string;
  email: string;
  dni?: string;
  phoneNumber?: string;
  photoUrl?: string;
  roles: string[];
}

export interface AuthResponse {
  token: string;
  userId: string;
  personId: string;
  fullName: string;
  email: string;
  roles: string[];
  expiresAtUtc: string;
}

export interface LoginRequest {
  email: string;
  passwordHash: string;
}

export interface RegisterRequest {
  firstName: string;
  lastName: string;
  dni: string;
  email: string;
  password: string;
  phoneNumber?: string;
}

export interface GoogleAuthRequest {
  idToken: string;
}
