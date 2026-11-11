(function () {
  'use strict';

  /**
   * Payload validation & suggestions — validates correspondence payload fields
   * and provides actionable suggestions.
   *
   * Levels: 'error' = blocking, 'warning' = optional fix, 'info' = enhancement.
   */
  angular.module('adminPortal').factory('payloadValidation', payloadValidation);

  var PATTERNS = {
    email: /^[^\s@]+@[^\s@]+\.[^\s@]+$/,
    phone: /^[+]?[(]?[0-9]{1,4}[)]?[-\s.]?[(]?[0-9]{1,4}[)]?[-\s.]?[0-9]{1,9}$/,
    zipCode: /^[0-9]{5}(-[0-9]{4})?$/, // US ZIP or ZIP+4
    claimNumber: /^[A-Z]{2,3}-[0-9]{3,6}$/i, // e.g. CL-12345
    policyNumber: /^[A-Z]{1,3}-[0-9]{4,8}$/i // e.g. PL-123456
  };

  var EMAIL_DOMAIN_TYPOS = {
    'gmial.com': 'gmail.com',
    'gmai.com': 'gmail.com',
    'yahooo.com': 'yahoo.com',
    'yaho.com': 'yahoo.com',
    'hotmial.com': 'hotmail.com',
    'outlok.com': 'outlook.com',
    'outlook.co': 'outlook.com'
  };

  var PLACEHOLDER_NAMES = ['test', 'name', 'customer', 'user', 'xxx', 'placeholder', 'unknown'];

  function blank(v) {
    return !v || String(v).trim() === '';
  }

  function issue(fieldName, level, message, suggestion, autoFix) {
    var result = { fieldName: fieldName, isValid: level === 'info', level: level, message: message, suggestion: suggestion };
    if (autoFix) result.autoFix = autoFix;
    return result;
  }

  function suggestEmailFix(email) {
    var parts = email.split('@');
    if (parts.length !== 2) return null;
    var fixedDomain = EMAIL_DOMAIN_TYPOS[parts[1].toLowerCase()];
    return fixedDomain ? parts[0] + '@' + fixedDomain : null;
  }

  function validateEmail(email) {
    if (blank(email)) {
      return issue('email', 'error', 'Email is required for electronic delivery', 'Provide the recipient\'s email address');
    }
    var trimmed = email.trim();
    if (!PATTERNS.email.test(trimmed)) {
      var autoFix = suggestEmailFix(trimmed);
      return issue('email', 'error', 'Invalid email format: "' + trimmed + '"',
        autoFix ? 'Did you mean: ' + autoFix + '?' : 'Enter a valid email address (e.g., user@domain.com)', autoFix);
    }
    return null;
  }

  function validatePhone(phone) {
    if (blank(phone)) {
      return issue('phone', 'warning', 'Phone number is missing', 'Adding a phone number enables SMS/call delivery options and improves reach');
    }
    var trimmed = phone.trim();
    if (!PATTERNS.phone.test(trimmed)) {
      return issue('phone', 'warning', 'Phone format may be invalid: "' + trimmed + '"', 'Use format like (555) 123-4567 or +1-555-123-4567');
    }
    return null;
  }

  function validateZipCode(zip) {
    if (blank(zip)) {
      return issue('zipCode', 'warning', 'ZIP code is missing', 'Adding ZIP code improves delivery accuracy for physical mail');
    }
    var trimmed = zip.trim().replace(/\s/g, '');
    if (!PATTERNS.zipCode.test(trimmed)) {
      return issue('zipCode', 'warning', 'Invalid ZIP code format: "' + trimmed + '"', 'Use US format: 12345 or 12345-6789');
    }
    return null;
  }

  function validateAddress(address) {
    if (blank(address)) {
      return issue('address', 'error', 'Street address is required for physical delivery', 'Enter the recipient\'s street address (e.g., "123 Main St")');
    }
    if (address.trim().length < 5) {
      return issue('address', 'error', 'Address is too short', 'Include street number and name');
    }
    return null;
  }

  function validateClaimNumber(claim) {
    if (blank(claim)) {
      return issue('claimNumber', 'error', 'Claim number is required', 'Enter the claim number (e.g., "CL-12345" or "CLM-987654")');
    }
    var trimmed = claim.trim().toUpperCase();
    if (!PATTERNS.claimNumber.test(trimmed)) {
      return issue('claimNumber', 'warning', 'Claim number format unusual: "' + trimmed + '"', 'Standard format is: 2-3 letters, hyphen, 3-6 digits (e.g., "CL-12345")');
    }
    return null;
  }

  function validatePolicyNumber(policy) {
    if (blank(policy)) return null; // optional
    var trimmed = policy.trim().toUpperCase();
    if (!PATTERNS.policyNumber.test(trimmed)) {
      return issue('policyNumber', 'warning', 'Policy number format unusual: "' + trimmed + '"', 'Standard format is: 1-3 letters, hyphen, 4-8 digits (e.g., "PL-123456")');
    }
    return null;
  }

  function validateRecipientName(name) {
    if (blank(name)) {
      return issue('recipientName', 'error', 'Recipient name is required', 'Enter the recipient\'s full name');
    }
    var trimmed = name.trim();
    if (trimmed.length < 2) {
      return issue('recipientName', 'error', 'Name is too short', 'Enter at least first and last name');
    }
    // Only obvious fakes, not real names.
    if (PLACEHOLDER_NAMES.indexOf(trimmed.toLowerCase()) !== -1) {
      return issue('recipientName', 'warning', 'Recipient name appears to be a placeholder: "' + trimmed + '"', 'Verify you entered the actual recipient name');
    }
    return null;
  }

  function validateCityState(city, state) {
    var issues = [];
    if (blank(city)) {
      issues.push(issue('city', 'warning', 'City is missing', 'Adding city improves delivery accuracy'));
    }
    if (blank(state)) {
      issues.push(issue('state', 'warning', 'State is missing', 'Enter the 2-letter state code (e.g., "IL" for Illinois)'));
    } else if (state.trim().length !== 2) {
      issues.push(issue('state', 'warning', 'State code should be 2 letters: "' + state.trim() + '"', 'Use 2-letter abbreviation (e.g., "IL", "CA", "NY")'));
    }
    return issues;
  }

  function validateDeliveryMethod(deliveryMethod) {
    if (blank(deliveryMethod)) {
      return issue('deliveryMethod', 'error', 'Delivery method is required', 'Select Email, SMS, Phone Call, or Physical Mail');
    }
    var lower = deliveryMethod.toLowerCase();
    var known = ['email', 'sms', 'phone', 'mail', 'physical'].some(function (m) { return lower.indexOf(m) !== -1; });
    if (!known) {
      return issue('deliveryMethod', 'error', 'Unknown delivery method: "' + deliveryMethod + '"', 'Choose from: Email, SMS, Phone, or Physical Mail');
    }
    return null;
  }

  function validateDocumentType(docType) {
    if (blank(docType)) {
      return issue('documentType', 'warning', 'Document type is missing', 'Specify the document type for better tracking (e.g., "Welcome Kit", "Claim Decision")');
    }
    return null;
  }

  /** First non-null of PascalCase / camelCase variants of a property. */
  function pick(obj, pascal) {
    if (!obj) return undefined;
    var camel = pascal.charAt(0).toLowerCase() + pascal.slice(1);
    return obj[pascal] !== undefined && obj[pascal] !== null ? obj[pascal] : obj[camel];
  }

  /**
   * Validates an entire correspondence payload. Accepts either { Process: {...} }
   * or the Process object itself, in PascalCase or camelCase.
   */
  function validatePayload(payload) {
    payload = payload || {};
    var errors = [];
    var warnings = [];
    var suggestions = [];

    var process = payload.Process || payload.process || null;
    if (!process && payload.ECCOTrackingClaimNumber) process = payload;
    process = process || {};

    var mailDelivery = pick(process, 'MailDeliveryInfo') || {};
    var address = pick(mailDelivery, 'RecipientAddress') || {};

    var recipientName = pick(mailDelivery, 'RecipientFullName') || '';
    var claimNumber = process.ECCOTrackingClaimNumber || process.eccoTrackingClaimNumber || '';
    var policyNumber = process.ECCOTrackingPolicyNumber || process.eccoTrackingPolicyNumber || '';
    var deliveryMethod = pick(process, 'DeliveryMethod') || '';
    var email = pick(mailDelivery, 'RecipientEmailAddress') || '';
    var phone = pick(mailDelivery, 'RecipientPhoneNumber') || '';
    var addressLine1 = pick(address, 'RecipientAddressLine1') || '';
    var city = pick(address, 'RecipientAddressCity') || '';
    var state = pick(address, 'RecipientAddressST') || '';
    var zipCode = pick(address, 'RecipientAddressZIP') || '';
    var documentType = pick(process, 'DocumentDescription') || '';
    var requestingSystem = pick(process, 'RequestingSystemName') || '';

    function withExtracted(v, value) {
      v.message = v.message + ' [extracted: "' + value + '"]';
      return v;
    }

    var nameValidation = validateRecipientName(recipientName);
    if (nameValidation) {
      withExtracted(nameValidation, recipientName);
      (nameValidation.level === 'error' ? errors : warnings).push(nameValidation);
    }

    var claimValidation = validateClaimNumber(claimNumber);
    if (claimValidation) {
      withExtracted(claimValidation, claimNumber);
      (claimValidation.level === 'error' ? errors : warnings).push(claimValidation);
    }

    if (blank(requestingSystem)) {
      errors.push(issue('requestingSystemName', 'error', 'Requesting system is required',
        'Name the customer system the request is for, e.g. "Claims System"'));
    }

    var deliveryValidation = validateDeliveryMethod(deliveryMethod);
    if (deliveryValidation) errors.push(withExtracted(deliveryValidation, deliveryMethod));

    // Conditional validation based on delivery method.
    var method = deliveryMethod.toLowerCase();

    if (method.indexOf('email') !== -1) {
      var emailValidation = validateEmail(email);
      if (emailValidation) errors.push(withExtracted(emailValidation, email));
    }

    if (method.indexOf('mail') !== -1) {
      var addressValidation = validateAddress(addressLine1);
      if (addressValidation) errors.push(withExtracted(addressValidation, addressLine1));

      var zipValidation = validateZipCode(zipCode);
      if (zipValidation) warnings.push(withExtracted(zipValidation, zipCode));

      validateCityState(city, state).forEach(function (v) {
        v.message = v.message + ' [city: "' + city + '", state: "' + state + '"]';
        warnings.push(v);
      });
    }

    if (method.indexOf('sms') !== -1 || method.indexOf('phone') !== -1) {
      var phoneValidation = validatePhone(phone);
      if (phoneValidation) errors.push(withExtracted(phoneValidation, phone));
    }

    // Optional fields.
    var policyValidation = validatePolicyNumber(policyNumber);
    if (policyValidation) warnings.push(policyValidation);

    var docTypeValidation = validateDocumentType(documentType);
    if (docTypeValidation) warnings.push(docTypeValidation);

    // Enhancement suggestions — only when the contact detail is actually missing.
    if (method.indexOf('sms') === -1 && method.indexOf('phone') === -1 && blank(phone)) {
      suggestions.push(issue('phone', 'info', 'Consider adding a phone number', 'Phone enables SMS and call delivery options for better reach'));
    }
    if (method.indexOf('email') === -1 && blank(email)) {
      suggestions.push(issue('email', 'info', 'Consider adding an email address', 'Email is the fastest delivery method with tracking'));
    }

    return {
      isValid: errors.length === 0,
      errors: errors,
      warnings: warnings,
      suggestions: suggestions,
      summary: {
        totalIssues: errors.length + warnings.length + suggestions.length,
        errorCount: errors.length,
        warningCount: warnings.length,
        suggestionCount: suggestions.length
      }
    };
  }

  function payloadValidation() {
    return {
      validatePayload: validatePayload,
      validateEmail: validateEmail,
      validatePhone: validatePhone,
      validateZipCode: validateZipCode,
      validateAddress: validateAddress,
      validateClaimNumber: validateClaimNumber,
      validatePolicyNumber: validatePolicyNumber,
      validateRecipientName: validateRecipientName,
      validateCityState: validateCityState,
      validateDeliveryMethod: validateDeliveryMethod,
      validateDocumentType: validateDocumentType
    };
  }
})();
