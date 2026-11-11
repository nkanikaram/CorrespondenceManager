import { useEffect, useState } from 'react';
import { Dialog, DialogActionsBar } from '@progress/kendo-react-dialogs';
import { Button } from '@progress/kendo-react-buttons';
import { Input } from '@progress/kendo-react-inputs';

type Props = {
    show: boolean;
    title?: string;
    message?: React.ReactNode;
    defaultValue?: string;
    confirmLabel?: string;
    cancelLabel?: string;
    onSubmit: (value: string) => void;
    onCancel: () => void;
};

// A Telerik/Kendo React Dialog standing in for window.prompt() — same reasoning as
// ConfirmDialog: themeable and consistent instead of the browser's native prompt popup.
export default function PromptDialog({
    show,
    title = 'Enter a value',
    message,
    defaultValue = '',
    confirmLabel = 'Save',
    cancelLabel = 'Cancel',
    onSubmit,
    onCancel,
}: Props) {
    const [value, setValue] = useState(defaultValue);

    useEffect(() => {
        if (show) setValue(defaultValue);
    }, [show, defaultValue]);

    if (!show) return null;

    const submit = () => {
        const trimmed = value.trim();
        if (trimmed) onSubmit(trimmed);
    };

    return (
        <Dialog title={title} onClose={onCancel} width={380}>
            {message && <div style={{ fontSize: 13, marginBottom: 10 }}>{message}</div>}
            <Input
                value={value}
                onChange={e => setValue(String(e.value ?? ''))}
                onKeyDown={e => { if (e.key === 'Enter') submit(); }}
                style={{ width: '100%' }}
                autoFocus
            />
            <DialogActionsBar layout="end">
                <Button type="button" onClick={onCancel}>
                    {cancelLabel}
                </Button>
                <Button type="button" themeColor="primary" disabled={!value.trim()} onClick={submit}>
                    {confirmLabel}
                </Button>
            </DialogActionsBar>
        </Dialog>
    );
}
