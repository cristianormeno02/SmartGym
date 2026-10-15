export interface UserAccountDto {
  hasAccount: boolean;
  userId?: string | null;
  username?: string | null;
  isActive?: boolean | null;
  lastLoginUtc?: string | null;
  roles: string[];
}

export interface ProvisionUserAccountRequest {
  password: string;
  roleIds?: number[];
}

export interface UpdateUserRolesRequest {
  roleIds?: number[];
}

export interface UpdateUserStatusRequest {
  isActive: boolean;
}

export interface AdminResetPasswordRequest {
  newPassword: string;
}

export const ROLE_DEFINITIONS = [
  { id: 1, name: 'Administrator', label: 'Administrador' },
  { id: 2, name: 'Secretary', label: 'Secretaria' },
  { id: 3, name: 'Instructor', label: 'Profesor' },
  { id: 4, name: 'Student', label: 'Alumno' }
] as const;
