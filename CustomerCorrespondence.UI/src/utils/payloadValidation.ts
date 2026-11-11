/**
 * Payload Validation & Suggestions
 * Validates correspondence payload fields and provides actionable suggestions
 */

export type ValidationLevel = 'error' | 'warning' | 'info';

export interface FieldValidation {
    fieldName: string;
    isValid: boolean;
    level: ValidationLevel; // 'error' = blocking, 'warning' = optional fix, 'info' = enhancement
    message: string;
    suggestion?: string;
    autoFix?: string; // Suggested corrected value
}

export interface PayloadValidationResult {
    isValid: boolean; // true only if no errors (warnings OK)
    errors: FieldValidation[];
    warnings: FieldValidation[];
    suggestions: FieldValidation[]; // Enhancement suggestions
    summary: {
        totalIssues: number;
        errorCount: number;
        warningCount: number;
        suggestionCount: number;
    };
}

// Regex patterns for validation
const PATTERNS = {
    email: /^[^\s@]+@[^\s@]+\.[^\s@]+$/,
    phone: /^[+]?[(]?[0-9]{1,4}[)]?[-\s.]?[(]?[0-9]{1,4}[)]?[-\s.]?[0-9]{1,9}$/,
    zipCode: /^[0-9]{5}(-[0-9]{4})?$/, // US ZIP or ZIP+4
    claimNumber: /^[A-Z]{2,3}-[0-9]{3,6}$/i, // e.g., CL-12345
    policyNumber: /^[A-Z]{1,3}-[0-9]{4,8}$/i, // e.g., PL-123456
};

/**
 * Validates an email address
 */
export function validateEmail(email: string | undefined): FieldValidation | null {
    if (!email || email.trim() === '') {
        return {
            fieldName: 'email',
            isValid: false,
            level: 'error',
            message: 'Email is required for electronic delivery',
            suggestion: 'Provide the recipient\'s email address',
        };
    }

    const trimmed = email.trim();
    if (!PATTERNS.email.test(trimmed)) {
        // Try common typos
        const autoFix = suggestEmailFix(trimmed);
        return {
            fieldName: 'email',
            isValid: false,
            level: 'error',
            message: `Invalid email format: "${trimmed}"`,
            suggestion: autoFix ? `Did you mean: ${autoFix}?` : 'Enter a valid email address (e.g., user@domain.com)',
            autoFix: autoFix ?? undefined,
        };
    }

    return null;
}

/**
 * Validates a phone number
 */
export function validatePhone(phone: string | undefined): FieldValidation | null {
    if (!phone || phone.trim() === '') {
        return {
            fieldName: 'phone',
            isValid: false,
            level: 'warning',
            message: 'Phone number is missing',
            suggestion: 'Adding a phone number enables SMS/call delivery options and improves reach',
        };
    }

    const trimmed = phone.trim();
    if (!PATTERNS.phone.test(trimmed)) {
        return {
            fieldName: 'phone',
            isValid: false,
            level: 'warning',
            message: `Phone format may be invalid: "${trimmed}"`,
            suggestion: 'Use format like (555) 123-4567 or +1-555-123-4567',
        };
    }

    return null;
}

/**
 * Validates a US ZIP code
 */
export function validateZipCode(zip: string | undefined): FieldValidation | null {
    if (!zip || zip.trim() === '') {
        return {
            fieldName: 'zipCode',
            isValid: false,
            level: 'warning',
            message: 'ZIP code is missing',
            suggestion: 'Adding ZIP code improves delivery accuracy for physical mail',
        };
    }

    const trimmed = zip.trim().replace(/\s/g, '');
    if (!PATTERNS.zipCode.test(trimmed)) {
        return {
            fieldName: 'zipCode',
            isValid: false,
            level: 'warning',
            message: `Invalid ZIP code format: "${trimmed}"`,
            suggestion: 'Use US format: 12345 or 12345-6789',
        };
    }

    return null;
}

/**
 * Validates a street address
 */
export function validateAddress(address: string | undefined): FieldValidation | null {
    if (!address || address.trim() === '') {
        return {
            fieldName: 'address',
            isValid: false,
            level: 'error',
            message: 'Street address is required for physical delivery',
            suggestion: 'Enter the recipient\'s street address (e.g., "123 Main St")',
        };
    }

    const trimmed = address.trim();
    if (trimmed.length < 5) {
        return {
            fieldName: 'address',
            isValid: false,
            level: 'error',
            message: 'Address is too short',
            suggestion: 'Include street number and name',
        };
    }

    return null;
}

/**
 * Validates a claim number format
 */
export function validateClaimNumber(claim: string | undefined): FieldValidation | null {
    if (!claim || claim.trim() === '') {
        return {
            fieldName: 'claimNumber',
            isValid: false,
            level: 'error',
            message: 'Claim number is required',
            suggestion: 'Enter the claim number (e.g., "CL-12345" or "CLM-987654")',
        };
    }

    const trimmed = claim.trim().toUpperCase();
    if (!PATTERNS.claimNumber.test(trimmed)) {
        return {
            fieldName: 'claimNumber',
            isValid: false,
            level: 'warning',
            message: `Claim number format unusual: "${trimmed}"`,
            suggestion: 'Standard format is: 2-3 letters, hyphen, 3-6 digits (e.g., "CL-12345")',
        };
    }

    return null;
}

/**
 * Validates a policy number format
 */
export function validatePolicyNumber(policy: string | undefined): FieldValidation | null {
    if (!policy || policy.trim() === '') {
        // Policy is optional
        return null;
    }

    const trimmed = policy.trim().toUpperCase();
    if (!PATTERNS.policyNumber.test(trimmed)) {
        return {
            fieldName: 'policyNumber',
            isValid: false,
            level: 'warning',
            message: `Policy number format unusual: "${trimmed}"`,
            suggestion: 'Standard format is: 1-3 letters, hyphen, 4-8 digits (e.g., "PL-123456")',
        };
    }

    return null;
}

/**
 * Validates recipient name
 */
export function validateRecipientName(name: string | undefined): FieldValidation | null {
    if (!name || name.trim() === '') {
        return {
            fieldName: 'recipientName',
            isValid: false,
            level: 'error',
            message: 'Recipient name is required',
            suggestion: 'Enter the recipient\'s full name',
        };
    }

    const trimmed = name.trim();
    if (trimmed.length < 2) {
        return {
            fieldName: 'recipientName',
            isValid: false,
            level: 'error',
            message: 'Name is too short',
            suggestion: 'Enter at least first and last name',
        };
    }

    // Check for common placeholder names (only obvious fakes, not real names)
    if (['test', 'name', 'customer', 'user', 'xxx', 'placeholder', 'unknown'].includes(trimmed.toLowerCase())) {
        return {
            fieldName: 'recipientName',
            isValid: false,
            level: 'warning',
            message: `Recipient name appears to be a placeholder: "${trimmed}"`,
            suggestion: 'Verify you entered the actual recipient name',
        };
    }

    return null;
}

/**
 * Validates city/state
 */
export function validateCityState(city: string | undefined, state: string | undefined): FieldValidation[] {
    const issues: FieldValidation[] = [];

    if (!city || city.trim() === '') {
        issues.push({
            fieldName: 'city',
            isValid: false,
            level: 'warning',
            message: 'City is missing',
            suggestion: 'Adding city improves delivery accuracy',
        });
    }

    if (!state || state.trim() === '') {
        issues.push({
            fieldName: 'state',
            isValid: false,
            level: 'warning',
            message: 'State is missing',
            suggestion: 'Enter the 2-letter state code (e.g., "IL" for Illinois)',
        });
    } else if (state.trim().length !== 2) {
        issues.push({
            fieldName: 'state',
            isValid: false,
            level: 'warning',
            message: `State code should be 2 letters: "${state.trim()}"`,
            suggestion: 'Use 2-letter abbreviation (e.g., "IL", "CA", "NY")',
        });
    }

    return issues;
}

/**
 * Validates delivery method selection
 */
export function validateDeliveryMethod(deliveryMethod: string | undefined): FieldValidation | null {
    if (!deliveryMethod || deliveryMethod.trim() === '') {
        return {
            fieldName: 'deliveryMethod',
            isValid: false,
            level: 'error',
            message: 'Delivery method is required',
            suggestion: 'Select Email, SMS, Phone Call, or Physical Mail',
        };
    }

    const validMethods = ['email', 'sms', 'phone', 'mail', 'physical'];
    if (!validMethods.some(m => deliveryMethod.toLowerCase().includes(m))) {
        return {
            fieldName: 'deliveryMethod',
            isValid: false,
            level: 'error',
            message: `Unknown delivery method: "${deliveryMethod}"`,
            suggestion: 'Choose from: Email, SMS, Phone, or Physical Mail',
        };
    }

    return null;
}

/**
 * Validates document type
 */
export function validateDocumentType(docType: string | undefined): FieldValidation | null {
    if (!docType || docType.trim() === '') {
        return {
            fieldName: 'documentType',
            isValid: false,
            level: 'warning',
            message: 'Document type is missing',
            suggestion: 'Specify the document type for better tracking (e.g., "Welcome Kit", "Claim Decision")',
        };
    }

    return null;
}

/**
 * Suggests email fix for common typos
 */
function suggestEmailFix(email: string): string | null {
    const commonDomainTypos: Record<string, string> = {
        'gmial.com': 'gmail.com',
        'gmai.com': 'gmail.com',
        'yahooo.com': 'yahoo.com',
        'yaho.com': 'yahoo.com',
        'hotmial.com': 'hotmail.com',
        'outlok.com': 'outlook.com',
        'outlook.co': 'outlook.com',
    };

    const parts = email.split('@');
    if (parts.length !== 2) return null;

    const [local, domain] = parts;
    const fixedDomain = commonDomainTypos[domain.toLowerCase()];
    return fixedDomain ? `${local}@${fixedDomain}` : null;
}

/**
 * Main validation function - validates entire payload
 * Extracts nested fields from the Process structure
 */
export function validatePayload(payload: any): PayloadValidationResult {
    const errors: FieldValidation[] = [];
    const warnings: FieldValidation[] = [];
    const suggestions: FieldValidation[] = [];

    // Extract fields from nested structure
    // Handle both cases: payload.Process or payload is Process
    let process = payload.Process ?? payload.process ?? null;
    if (!process && payload.ECCOTrackingClaimNumber) {
        // payload IS the Process object itself
        process = payload;
    }
    process = process ?? {};

    const mailDelivery = process.MailDeliveryInfo ?? process.mailDeliveryInfo ?? {};
    const address = mailDelivery.RecipientAddress ?? mailDelivery.recipientAddress ?? {};

    // Extracted values
    const recipientName = mailDelivery.RecipientFullName ?? mailDelivery.recipientFullName ?? '';
    const claimNumber = process.ECCOTrackingClaimNumber ?? process.eccoTrackingClaimNumber ?? '';
    const policyNumber = process.ECCOTrackingPolicyNumber ?? process.eccoTrackingPolicyNumber ?? '';
    const deliveryMethod = process.DeliveryMethod ?? process.deliveryMethod ?? '';
    const email = mailDelivery.RecipientEmailAddress ?? mailDelivery.recipientEmailAddress ?? '';
    const phone = mailDelivery.RecipientPhoneNumber ?? mailDelivery.recipientPhoneNumber ?? '';
    const addressLine1 = address.RecipientAddressLine1 ?? address.recipientAddressLine1 ?? '';
    const city = address.RecipientAddressCity ?? address.recipientAddressCity ?? '';
    const state = address.RecipientAddressST ?? address.recipientAddressST ?? '';
    const zipCode = address.RecipientAddressZIP ?? address.recipientAddressZIP ?? '';
    const documentType = process.DocumentDescription ?? process.documentDescription ?? '';

    // Debug logging with extracted values for troubleshooting
    const extractedDebug = {
        recipientName: `"${recipientName}"`,
        claimNumber: `"${claimNumber}"`,
        deliveryMethod: `"${deliveryMethod}"`,
        email: `"${email}"`,
        documentType: `"${documentType}"`,
    };
    console.log('🔍 validatePayload - Extracted fields:', extractedDebug);
    console.log('Raw payload structure:', {
        hasProcess: !!payload.Process,
        hasMailDelivery: !!mailDelivery,
        hasAddress: !!address,
        processKeys: Object.keys(process).slice(0, 10),
        mailDeliveryKeys: Object.keys(mailDelivery).slice(0, 5),
    });

    // Validate required fields
    const nameValidation = validateRecipientName(recipientName);
    if (nameValidation) {
        nameValidation.message = `${nameValidation.message} [extracted: "${recipientName}"]`;
        if (nameValidation.level === 'error') errors.push(nameValidation);
        else if (nameValidation.level === 'warning') warnings.push(nameValidation);
    }

    const claimValidation = validateClaimNumber(claimNumber);
    if (claimValidation) {
        claimValidation.message = `${claimValidation.message} [extracted: "${claimNumber}"]`;
        if (claimValidation.level === 'error') errors.push(claimValidation);
        else warnings.push(claimValidation);
    }

    const deliveryValidation = validateDeliveryMethod(deliveryMethod);
    if (deliveryValidation) {
        deliveryValidation.message = `${deliveryValidation.message} [extracted: "${deliveryMethod}"]`;
        errors.push(deliveryValidation);
    }

    // Conditional validation based on delivery method
    const method = deliveryMethod?.toLowerCase() || '';

    if (method.includes('email')) {
        const emailValidation = validateEmail(email);
        if (emailValidation) {
            emailValidation.message = `${emailValidation.message} [extracted: "${email}"]`;
            errors.push(emailValidation);
        }
    }

    if (method.includes('mail')) {
        const addressValidation = validateAddress(addressLine1);
        if (addressValidation) {
            addressValidation.message = `${addressValidation.message} [extracted: "${addressLine1}"]`;
            errors.push(addressValidation);
        }

        const zipValidation = validateZipCode(zipCode);
        if (zipValidation) {
            zipValidation.message = `${zipValidation.message} [extracted: "${zipCode}"]`;
            warnings.push(zipValidation);
        }

        const cityStateValidations = validateCityState(city, state);
        cityStateValidations.forEach(v => {
            v.message = `${v.message} [city: "${city}", state: "${state}"]`;
            warnings.push(v);
        });
    }

    if (method.includes('sms') || method.includes('phone')) {
        const phoneValidation = validatePhone(phone);
        if (phoneValidation) {
            phoneValidation.message = `${phoneValidation.message} [extracted: "${phone}"]`;
            errors.push(phoneValidation);
        }
    }

    // Optional field validation
    const policyValidation = validatePolicyNumber(policyNumber);
    if (policyValidation) warnings.push(policyValidation);

    const docTypeValidation = validateDocumentType(documentType);
    if (docTypeValidation) warnings.push(docTypeValidation);

    // Enhancement suggestions
    const phoneCheck = validatePhone(phone);
    if (!method.includes('sms') && !method.includes('phone') && !phoneCheck) {
        suggestions.push({
            fieldName: 'phone',
            isValid: true,
            level: 'info',
            message: 'Consider adding a phone number',
            suggestion: 'Phone enables SMS and call delivery options for better reach',
        });
    }

    const emailCheck = validateEmail(email);
    if (!method.includes('email') && !emailCheck) {
        suggestions.push({
            fieldName: 'email',
            isValid: true,
            level: 'info',
            message: 'Consider adding an email address',
            suggestion: 'Email is the fastest delivery method with tracking',
        });
    }

    return {
        isValid: errors.length === 0,
        errors,
        warnings,
        suggestions,
        summary: {
            totalIssues: errors.length + warnings.length + suggestions.length,
            errorCount: errors.length,
            warningCount: warnings.length,
            suggestionCount: suggestions.length,
        },
    };
}
