export function applyTheme(values) {
    for (const [name, value] of Object.entries(values)) document.documentElement.style.setProperty(name, value);
}
export async function copyText(value) { await navigator.clipboard.writeText(value); }
