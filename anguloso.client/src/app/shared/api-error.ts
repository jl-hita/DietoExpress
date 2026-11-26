/**
 * Texto que se puede enseñar al usuario a partir de un error de la API. El servidor responde unas veces con texto, otras con
 * { message } o { error } y, ante un fallo inesperado, con un ProblemDetails; mostrar el cuerpo tal cual producía «[object Object]».
 */
export function apiErrorMessage(error: unknown, fallback: string): string {
  const body = (error as { error?: unknown } | null | undefined)?.error;
  if (typeof body === 'string') {
    const text = body.trim();
    // Una página HTML (502 del proxy, por ejemplo) o un volcado largo no es un mensaje.
    return text && !text.startsWith('<') && text.length <= 300 ? text : fallback;
  }
  const { message, error: detail } = (body ?? {}) as { message?: unknown; error?: unknown };
  const text = [message, detail].find(value => typeof value === 'string' && value.trim());
  return typeof text === 'string' ? text : fallback;
}
