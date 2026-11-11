import axios from 'axios';
import { getChatUserId } from '../utils/chatUserId';

const api = axios.create({
    baseURL: import.meta.env.VITE_API_BASE_URL ?? '/api',
    timeout: 15000,
});

export type ConversationSummary = {
    conversationId: string;
    title: string | null;
    createdTS: string;
    updatedTS: string;
};

export type ChatMessage = {
    messageId: string;
    role: 'user' | 'assistant';
    content: string;
    createdTS: string;
};

export type ConversationDetail = ConversationSummary & {
    messages: ChatMessage[];
};

export async function listConversations(): Promise<ConversationSummary[]> {
    const res = await api.get('/chathistory/conversations', { params: { userId: getChatUserId() } });
    return res.data;
}

export async function getConversation(conversationId: string): Promise<ConversationDetail> {
    const res = await api.get(`/chathistory/conversations/${conversationId}`);
    return res.data;
}

export async function createConversation(title?: string): Promise<{ conversationId: string }> {
    const res = await api.post('/chathistory/conversations', { userId: getChatUserId(), title });
    return res.data;
}

export async function appendMessage(conversationId: string, role: 'user' | 'assistant', content: string): Promise<void> {
    await api.post(`/chathistory/conversations/${conversationId}/messages`, { role, content });
}

export async function deleteConversation(conversationId: string): Promise<void> {
    await api.delete(`/chathistory/conversations/${conversationId}`);
}
