# AI Enhancements - Test Cases

## Phase 1: AI-Powered Payload Generation

### Test Steps
1. Open the Correspondence Manager application
2. Click the 💬 AI Assistant button (bottom-right corner)
3. Enter natural language request: "Send a confirmation letter to John Smith for claim CL-12345 about auto policy POLICY-789"
4. Wait for AI to generate the correspondence request
5. Review the JSON payload in the modal that appears
6. Optionally edit the payload fields
7. Click "Submit" button

### Detailed Test Scenarios

| # | Scenario | Expected Result | Pass/Fail |
|---|----------|-----------------|-----------|
| 1.1 | AI extracts all fields correctly | Modal shows: recipient=John Smith, claim=CL-12345, policy=POLICY-789 | |
| 1.2 | User can edit payload before submit | JSON textarea allows text editing | |
| 1.3 | Submit button sends payload to backend | API call made with complete JSON structure | |
| 1.4 | Modal closes after successful submit | Modal hidden, grid refreshes with new record | |
| 1.5 | Error on invalid JSON | Submit blocked, validation error shown | |
| 1.6 | Multiple fields in natural language | All identified fields populated in payload | |

---

## Phase 2: Smart Error Recovery

### Test Steps
1. Ensure backend/network is temporarily unavailable or returns error
2. Perform a search or AI request
3. Observe SmartErrorAlert component appears
4. Check error classification badge (📡 network, 🔧 server, ⚠️ validation, ❌ unknown)
5. Read recovery suggestion with 💡 icon
6. Click "Retry" button
7. Wait for exponential backoff: 500ms → 1s → 2s → 4s
8. After backoff, request retries automatically

### Detailed Test Scenarios

| # | Scenario | Expected Result | Pass/Fail |
|---|----------|-----------------|-----------|
| 2.1 | Network timeout error (>30s) | Alert shows 📡 Network & suggests "Check connection" | |
| 2.2 | 500 Server error | Alert shows 🔧 Server & suggests "Try again later" | |
| 2.3 | 400 Validation error | Alert shows ⚠️ Validation & suggests "Fix input values" | |
| 2.4 | Unknown error code | Alert shows ❌ Unknown & generic suggestion | |
| 2.5 | Retry button with backoff | First retry at 500ms, exponential growth to 4s max | |
| 2.6 | Retry success | After successful retry, alert dismissed, data loads | |
| 2.7 | Max retries exceeded | Alert shows permanent error, manual retry required | |
| 2.8 | Retryable errors identified | Network/timeout errors marked retryable=true | |

---

## Phase 3: Payload Validation & Suggestions

### Test Steps
1. Open AI Assistant and generate a payload
2. Modal appears with JSON textarea
3. Intentionally enter invalid data:
   - Email: "john@gmial.com" (typo)
   - Phone: "123" (incomplete)
   - ZIP: "1234" (too short)
   - Recipient: "test" (placeholder name)
4. Observe PayloadValidationDisplay in real-time
5. Click "Apply Fix" buttons for auto-correctable errors
6. Correct non-auto-fixable errors manually
7. Submit only when validation shows green checkmark

### Detailed Test Scenarios

| # | Scenario | Expected Result | Pass/Fail |
|---|----------|-----------------|-----------|
| 3.1 | Email typo detection | "gmial.com" detected, suggest "gmail.com" | |
| 3.2 | Email auto-fix applied | Click "Apply Fix" corrects to gmail.com | |
| 3.3 | Phone validation | "123" marked as error, suggest "10+ digits" | |
| 3.4 | ZIP validation | "1234" marked as error, suggest "5 or 9 digits" | |
| 3.5 | Placeholder name rejection | "test", "john doe", "user" rejected as invalid | |
| 3.6 | Real name accepted | "John Smith" accepted, no error | |
| 3.7 | Field-level indicators | Red 🔴 for errors, yellow 🟡 for warnings, blue 💡 for suggestions | |
| 3.8 | Submit blocked on errors | Submit button disabled until all errors fixed | |
| 3.9 | Submit enabled on success | Submit button enabled with green 💚 validation badge | |
| 3.10 | Multiple errors shown | Summary badge shows "3 Errors, 1 Warning, 2 Suggestions" | |

---

## Phase 4: Intelligent Search Refinement

### Test Steps
1. Search page loaded with grid empty
2. Enter search text with common typo: "cliam" (should be "claim")
3. Click Search or press Enter
4. Observe SearchRefinementPanel appears above grid
5. Panel shows "Did you mean: claim?" with Fix Typo button
6. Click "Fix Typo" to apply correction
7. Search re-runs with corrected term
8. Perform multiple searches to build history
9. Collapse/expand panel using header toggle
10. Check localStorage in DevTools for persisted history

### Detailed Test Scenarios

| # | Scenario | Expected Result | Pass/Fail |
|---|----------|-----------------|-----------|
| 4.1 | Typo detection - "cliam" | Suggests "claim" with 🔤 icon | |
| 4.2 | Typo detection - "polic" | Suggests "policy" | |
| 4.3 | Typo fix button click | Search re-runs with corrected term | |
| 4.4 | Filter suggestion - "failed" | Shows "Show Failed Requests" filter suggestion | |
| 4.5 | Filter suggestion - "recent" | Shows "Sort by Date" filter suggestion | |
| 4.6 | Filter apply button | Clicking re-searches with filter applied | |
| 4.7 | Related search suggestion | Searching "email" suggests "delivery", "sent", "status" | |
| 4.8 | Related search apply | Clicking related search re-searches that term | |
| 4.9 | Search history tracking | Previous 3-5 searches displayed in panel | |
| 4.10 | History click to repeat | Clicking badge repeats that search with same params | |
| 4.11 | Panel collapse/expand | Toggle button hides/shows all suggestions | |
| 4.12 | Suggestion count badge | Shows correct number of suggestions (e.g., "5 Suggestions") | |
| 4.13 | localStorage persistence | Close browser, reopen → history still visible | |
| 4.14 | Pattern learning | Frequently used filters tracked and suggested next time | |
| 4.15 | Session history | sessionStorage keeps searches within session | |
| 4.16 | Dismiss suggestions | Click ✕ button removes individual suggestion | |

---

## Integration Test Cases

### Test Steps
1. Complete flow: AI generation → validation → error recovery → search refinement
2. Generate payload with AI
3. Introduce validation error (typo email)
4. Apply fix suggestion
5. Submit with temporary error
6. Observe error recovery
7. Retry successful
8. Search for similar correspondence
9. Use search refinement suggestions
10. Verify all 4 features work together seamlessly

### Detailed Test Scenarios

| # | Scenario | Expected Result | Pass/Fail |
|---|----------|-----------------|-----------|
| I.1 | Full workflow chain | All 4 phases work without conflicts | |
| I.2 | Error recovery after validation | If submit fails, error recovery dialog appears | |
| I.3 | Search history shows AI-generated records | Generated correspondence appears in search results | |
| I.4 | Typo suggestions for search terms | Same typo detection as query refinement | |
| I.5 | No duplicate functionality | No conflicting or duplicate features | |
| I.6 | All 4 features in dark theme | UI readable in dark mode (VS Code dark colors) | |
| I.7 | Responsive layout | Components stack properly on smaller screens | |
| I.8 | Performance - no lag | Modal opens <300ms, search <1s, refinement <100ms | |

---

## Test Checklist

### Before Testing
- [ ] Dev server running (`npm run dev`)
- [ ] Build passes (`npm run build`)
- [ ] Backend API available
- [ ] Browser DevTools open (Network + Console tabs)
- [ ] localStorage Inspector available

### During Testing
- [ ] Check browser console for errors
- [ ] Monitor Network tab for API calls
- [ ] Verify localStorage entries created
- [ ] Check validation messages display correctly
- [ ] Confirm error retry backoff timing

### After Testing
- [ ] Clear localStorage (`localStorage.clear()` in console)
- [ ] Clear sessionStorage (`sessionStorage.clear()`)
- [ ] Close dev server
- [ ] Document any failures
- [ ] Mark pass/fail for each scenario

---

## Test Results Template

```
Phase: [1/2/3/4/Integration]
Date: [YYYY-MM-DD]
Tester: [Name]
Environment: Dev Server Port 5179
Browser: [Chrome/Edge/Firefox]

Scenarios Passed: [#]
Scenarios Failed: [#]
Scenarios Skipped: [#]

Failed Scenarios:
- [Scenario #]: [Description of failure]

Notes:
[Any additional observations]
```

---

## Known Limitations

1. **Phase 1**: AI-generated payloads may require field adjustments for complex scenarios
2. **Phase 2**: Error recovery only works for retryable errors (network/timeout); validation errors require user fix
3. **Phase 3**: Some misspellings may not be in the COMMON_TYPOS map; extensible for additional patterns
4. **Phase 4**: Search history limited to 20 items; older searches removed
5. **Integration**: All features require backend to be responsive; graceful degradation if backend unavailable

---

## Browser Compatibility

| Feature | Chrome | Edge | Firefox | Safari |
|---------|--------|------|---------|--------|
| Phase 1: AI Generation | ✅ | ✅ | ✅ | ⚠️ |
| Phase 2: Error Recovery | ✅ | ✅ | ✅ | ✅ |
| Phase 3: Validation | ✅ | ✅ | ✅ | ✅ |
| Phase 4: Search Refinement | ✅ | ✅ | ✅ | ⚠️ |
| localStorage Persistence | ✅ | ✅ | ✅ | ✅ |

*Note: ⚠️ = May have minor styling issues; Feature still functional*
