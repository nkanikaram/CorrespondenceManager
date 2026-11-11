import { useState, useEffect } from 'react';
import { Modal, Button, Form, Alert, Spinner } from 'react-bootstrap';
import { insertCorrespondence } from '../api/correspondenceApi';
import PayloadValidationDisplay from './PayloadValidationDisplay';
import { validatePayload } from '../utils/payloadValidation';
import type { PayloadValidationResult } from '../utils/payloadValidation';

const TEMPLATE = JSON.stringify({
    Process: {
        DocId: '',
        RequestingSystemName: '',
        ECCOTrackingClaimNumber: '',
        ECCOTrackingCustomerId: '',
        ECCOTrackingPolicyNumber: '',
        DeliveryMethod: 'Mail',
        DocumentDescription: '',
        MailDeliveryInfo: {
            RecipientFullName: '',
            RecipientFirstName: '',
            RecipientLastName: '',
            RecipientAddress: {
                RecipientAddressLine1: '',
                RecipientAddressLine2: null,
                RecipientAddressLine3: null,
                RecipientAddressCity: '',
                RecipientAddressST: '',
                RecipientAddressZIP: '',
                RecipientCountry: 'US',
            },
            NonUSAddressFlag: false,
            BadAddressFlag: false,
        },
        CompositionInfo: {
            VersionCode: '',
            CompositionSchemaId: '',
            SolutionsId: '',
        },
        AdditionalData: {},
    },
}, null, 2);

type Props = {
    show: boolean;
    onHide: () => void;
    onSuccess: (message: string) => void;
    onError: (message: string) => void;
    aiPayload?: string | null;
    aiContext?: string | null;
};

export default function InsertCorrespondenceModal({ show, onHide, onSuccess, onError, aiPayload, aiContext }: Props) {
    const [json, setJson] = useState(aiPayload || TEMPLATE);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [success, setSuccess] = useState(false);
    const [validation, setValidation] = useState<PayloadValidationResult | null>(null);

    // Validate payload whenever JSON changes
    const validateCurrentPayload = (jsonString: string) => {
        try {
            const parsed = JSON.parse(jsonString);
            const result = validatePayload(parsed);
            setValidation(result);
            return result;
        } catch {
            // JSON parse error - will be handled during submission
            setValidation(null);
        }
    };

    // Update json when aiPayload changes (AI-generated payload received)
    useEffect(() => {
        if (aiPayload && show) {
            setJson(aiPayload);
            setError(null);
            setSuccess(false);
            // Validate immediately after AI payload generation
            validateCurrentPayload(aiPayload);
        }
    }, [aiPayload, show]);

    function handleHide() {
        if (loading) return;
        setError(null);
        setSuccess(false);
        setJson(TEMPLATE);
        setValidation(null);
        onHide();
    }

    async function handleSubmit() {
        setError(null);
        setSuccess(false);

        // Validate before submission
        const validationResult = validateCurrentPayload(json);
        if (validationResult && !validationResult.isValid) {
            setError('Please fix the blocking errors before submitting.');
            return;
        }

        let parsed: unknown;
        try {
            parsed = JSON.parse(json);
        } catch {
            setError('Invalid JSON — please check the payload.');
            return;
        }
        setLoading(true);
        try {
            const resp = await insertCorrespondence(parsed);
            const docId = resp?.docId ?? resp?.DocId;
            const message = docId
                ? `Correspondence request accepted — DocId: ${docId}`
                : 'Correspondence request accepted for processing.';
            setSuccess(true);
            onSuccess(message);
        } catch (err: any) {
            const msg = err?.response?.data?.Message ?? err?.response?.data?.message ?? err?.message ?? 'Request failed';
            setError(String(msg));
            onError(String(msg));
        } finally {
            setLoading(false);
        }
    }

    function handleApplySuggestion(fieldName: string, autoFix: string) {
        try {
            const parsed = JSON.parse(json);
            // Map field names to JSON payload paths
            const fieldMap: Record<string, string[]> = {
                email: ['Process', 'MailDeliveryInfo', 'RecipientEmailAddress'],
                recipientName: ['Process', 'MailDeliveryInfo', 'RecipientFullName'],
                claimNumber: ['Process', 'ECCOTrackingClaimNumber'],
                policyNumber: ['Process', 'ECCOTrackingPolicyNumber'],
            };
            const path = fieldMap[fieldName];
            if (path) {
                let current: any = parsed;
                for (let i = 0; i < path.length - 1; i++) {
                    if (!current[path[i]]) current[path[i]] = {};
                    current = current[path[i]];
                }
                current[path[path.length - 1]] = autoFix;
                const newJson = JSON.stringify(parsed, null, 2);
                setJson(newJson);
                validateCurrentPayload(newJson);
            }
        } catch {
            // If JSON is invalid, can't apply fix
        }
    }

    return (
        <Modal show={show} onHide={handleHide} size="lg" centered>
            <Modal.Header closeButton>
                <Modal.Title style={{ fontSize: 16 }}>Insert Correspondence Request</Modal.Title>
            </Modal.Header>
            <Modal.Body>
                {aiContext && (
                    <Alert variant="info" className="py-2 mb-3">
                        <strong>✨ AI-Generated Payload</strong>
                        <div style={{ fontSize: 12, whiteSpace: 'pre-wrap', marginTop: '8px' }}>
                            {aiContext}
                        </div>
                        <small style={{ display: 'block', marginTop: '8px', color: '#666' }}>
                            Review and modify the JSON below as needed, then submit.
                        </small>
                    </Alert>
                )}
                {success && (
                    <Alert variant="success" className="py-2">
                        Correspondence inserted successfully.
                    </Alert>
                )}
                {error && (
                    <Alert variant="danger" className="py-2">
                        {error}
                    </Alert>
                )}

                {/* Validation Feedback */}
                <PayloadValidationDisplay
                    validation={validation}
                    onApplySuggestion={handleApplySuggestion}
                    jsonPayload={json}
                />

                <Form.Label style={{ fontSize: 12, color: '#555' }}>
                    JSON Payload — POST to <code>/api/CommManager/processcorrespondencerequest</code>
                </Form.Label>
                <Form.Control
                    as="textarea"
                    rows={16}
                    style={{ fontFamily: 'Consolas, "Courier New", monospace', fontSize: 13 }}
                    value={json}
                    onChange={e => {
                        setJson(e.target.value);
                        setError(null);
                        setSuccess(false);
                        validateCurrentPayload(e.target.value);
                    }}
                    disabled={loading}
                />
            </Modal.Body>
            <Modal.Footer>
                <Button variant="secondary" onClick={handleHide} disabled={loading}>Cancel</Button>
                <Button
                    variant="primary"
                    onClick={handleSubmit}
                    disabled={loading || !json.trim() || (validation?.isValid === false) || false}
                    title={validation && !validation.isValid ? 'Please fix the blocking errors first' : ''}
                >
                    {loading ? <><Spinner animation="border" size="sm" className="me-2" />Submitting…</> : 'Submit'}
                </Button>
            </Modal.Footer>
        </Modal>
    );
}
