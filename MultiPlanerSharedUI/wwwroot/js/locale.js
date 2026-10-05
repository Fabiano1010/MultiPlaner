export function getTimeZone() {
    return Intl.DateTimeFormat().resolvedOptions().timeZone;
}

export function getCountryCode() {
    // "pl-PL" -> "PL". Może zwrócić null, jeśli region nie jest w tagu (np. "en").
    const locale = navigator.language || navigator.languages?.[0];
    const parts = locale?.split("-");
    return parts?.length > 1 ? parts[1].toUpperCase() : null;
}