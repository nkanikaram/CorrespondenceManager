import { Dialog, DialogActionsBar } from '@progress/kendo-react-dialogs';
import { Button } from '@progress/kendo-react-buttons';

type Props = {
    show: boolean;
    title?: string;
    message: React.ReactNode;
    confirmLabel?: string;
    cancelLabel?: string;
    /** Renders the confirm button in the destructive (red) style for actions like delete. */
    danger?: boolean;
    onConfirm: () => void;
    onCancel: () => void;
};

// A Telerik/Kendo React Dialog standing in for window.confirm() — themeable, accessible, and
// consistent with the rest of the app's UI instead of the browser's native confirm popup.
export default function ConfirmDialog({
    show,
    title = 'Confirm',
    message,
    confirmLabel = 'Confirm',
    cancelLabel = 'Cancel',
    danger = false,
    onConfirm,
    onCancel,
}: Props) {
    if (!show) return null;
    return (
        <Dialog title={title} onClose={onCancel} width={380}>
            <div style={{ fontSize: 13, lineHeight: 1.5 }}>{message}</div>
            <DialogActionsBar layout="end">
                <Button type="button" onClick={onCancel}>
                    {cancelLabel}
                </Button>
                <Button type="button" themeColor={danger ? 'error' : 'primary'} onClick={onConfirm}>
                    {confirmLabel}
                </Button>
            </DialogActionsBar>
        </Dialog>
    );
}
