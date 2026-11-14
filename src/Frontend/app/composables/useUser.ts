export interface User { isAuthenticated: boolean, name: string | null, roles: string[] }

/**
 * Wer ist angemeldet? Fragt das BFF – Tokens sieht das Frontend nie, nur Name und Rollen.
 * Nur im Browser geladen: Beim Server-Rendering ginge das Session-Cookie nicht mit.
 */
export function useUser() {
  const user = useState<User | null>('user', () => null)
  onMounted(async () => {
    if (!user.value) user.value = await $fetch<User>('/api/bff/user').catch(() => null)
  })
  const hasRole = (role: string) => user.value?.roles.includes(role) ?? false
  return { user, hasRole }
}
