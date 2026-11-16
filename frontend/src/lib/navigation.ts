/** Only same-site paths are followed after sign-in, so a crafted link can't send people elsewhere. */
export function safeNext(value: string | null): string | null {
  return value && value.startsWith('/') && !value.startsWith('//') && !value.startsWith('/\\') ? value : null
}
