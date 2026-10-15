import { HttpErrorResponse } from '@angular/common/http';
import { describe, expect, it } from 'vitest';
import { apiErrorMessage } from './api-error';

describe('apiErrorMessage', () => {
  it('muestra los mensajes de validación en lugar del mensaje genérico', () => {
    const err = new HttpErrorResponse({
      status: 400,
      error: {
        message: 'Se produjeron uno o más errores de validación.',
        errors: {
          Password: ['La contraseña debe tener al menos 8 caracteres, al menos una letra mayúscula, un número y un carácter especial.'],
          RoleIds: ['Los roles asignables sólo pueden ser Administrador (1), Secretaria (2), Profesor (3) o Alumno (4).']
        }
      }
    });

    expect(apiErrorMessage(err, 'fallback')).toBe(
      'La contraseña debe tener al menos 8 caracteres, al menos una letra mayúscula, un número y un carácter especial. ' +
      'Los roles asignables sólo pueden ser Administrador (1), Secretaria (2), Profesor (3) o Alumno (4).'
    );
  });

  it('omite mensajes de validación repetidos', () => {
    const err = new HttpErrorResponse({
      status: 400,
      error: { message: 'x', errors: { Password: ['Débil.', 'Débil.'], NewPassword: ['Débil.'] } }
    });

    expect(apiErrorMessage(err, 'fallback')).toBe('Débil.');
  });

  it('usa el mensaje del servidor cuando no hay errores de validación', () => {
    const err = new HttpErrorResponse({ status: 409, error: { message: 'No se puede revocar al último administrador.' } });

    expect(apiErrorMessage(err, 'fallback')).toBe('No se puede revocar al último administrador.');
  });

  it('usa el mensaje por defecto cuando la respuesta no trae mensaje', () => {
    const err = new HttpErrorResponse({ status: 500, error: null });

    expect(apiErrorMessage(err, 'fallback')).toBe('fallback');
  });
});
