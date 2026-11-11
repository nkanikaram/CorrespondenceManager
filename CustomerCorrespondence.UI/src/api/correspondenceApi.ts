import axios from 'axios';
import { classifyError } from '../utils/errorRecovery';

const api = axios.create({
    baseURL: import.meta.env.VITE_API_BASE_URL ?? '/api',
    timeout: 30000,
});

// Add error interceptor to classify errors
api.interceptors.response.use(
    res => res,
    err => {
        // Attach error classification to the error object
        const classified = classifyError(err);
        return Promise.reject({
            ...err,
            ...classified,
        });
    }
);

export type SearchRequest = {
    freeText?: string;
    correspondenceId?: string;
    docId?: string;
    claimNumber?: string;
    policyNumber?: string;
    customerName?: string;
    customerId?: string;
    latestStatus?: string;
    latestStatuses?: string[];
    deliveryMethodCode?: string;
    fromDate?: string;
    toDate?: string;
    page?: number;
    pageSize?: number;
    sortBy?: string;
    sortDirection?: 'asc' | 'desc';
    includePayload?: boolean;
};

export async function searchCorrespondence(req: SearchRequest) {
    const res = await api.post('/correspondence/search', req);
    return res.data;
}

export async function insertCorrespondence(payload: unknown) {
    const res = await api.post('/CommManager/processcorrespondencerequest', payload);
    return res.data;
}

// The authoritative list of status values that actually exist in the data — used to let the AI
// validate a status name (or suggest the closest match) instead of guessing from training data.
export async function getStatusValues(): Promise<string[]> {
    const res = await api.get('/correspondence/statuses');
    return res.data;
}

export type AiUsageSummary = { inputTokens: number; outputTokens: number; claudeCalls: number; routerHits: number };

export async function getAiUsageSummary(): Promise<AiUsageSummary> {
    const res = await api.get('/Ai/usage/summary');
    return res.data;
}