const CHAT_USER_ID_KEY = 'ecco.ai.chatUserId';

// Persistent per-browser identifier for the AI assistant's chat history.
// The frontend has no auth token flow today, so we can't key history off a
// real logged-in user — this is a stand-in until one exists.
export function getChatUserId(): string {
    try {
        const existing = localStorage.getItem(CHAT_USER_ID_KEY);
        if (existing) return existing;
        const id = crypto.randomUUID();
        localStorage.setItem(CHAT_USER_ID_KEY, id);
        return id;
    } catch {
        return 'anonymous';
    }
}
