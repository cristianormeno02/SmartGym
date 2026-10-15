import { HttpErrorResponse } from '@angular/common/http';

/**
 * Mensaje para mostrar ante un error de la API. Los 400 de validación traen un `message` genérico
 * y el motivo concreto en `errors`, por eso se priorizan esos mensajes.
 */
export function apiErrorMessage(err: HttpErrorResponse, fallback: string): string {
  const errors = err.error?.errors as Record<string, string[]> | undefined;
  if (errors && typeof errors === 'object') {
    const messages = [...new Set(Object.values(errors).flat().filter(Boolean))];
    if (messages.length > 0) {
      return messages.join(' ');
    }
  }

  return err.error?.message || fallback;
}
