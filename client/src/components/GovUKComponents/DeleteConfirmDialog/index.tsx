import React, { useEffect, useId, useLayoutEffect, useRef, useState } from 'react';
import GovUKButton from '../GovUKButton';
import './styles.css';

interface DeleteConfirmDialogProps {
  open: boolean;
  title: string;
  message: string;
  itemName?: string;
  onConfirm: () => void;
  onCancel: () => void;
  isLoading?: boolean;
  testId?: string;
  confirmLabel?: string;
  children?: React.ReactNode;
}

// Read on each mount rather than once at import, so the fallback can be exercised by deleting showModal in devtools.
const canShowModal = (): boolean => typeof HTMLDialogElement !== 'undefined' && typeof HTMLDialogElement.prototype.showModal === 'function';

const OpenDialog = ({
  title,
  message,
  itemName,
  onConfirm,
  onCancel,
  isLoading = false,
  testId = 'delete-confirm-dialog',
  confirmLabel = 'Remove',
  children,
}: Omit<DeleteConfirmDialogProps, 'open'>): React.ReactElement => {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const [isModal] = useState(canShowModal);
  const titleId = useId();
  const messageId = useId();
  // Set by the unmount cleanup, so the close event its own close() queues is not mistaken for the user leaving.
  const closingRef = useRef(false);

  // The native Escape listener the fallback needs is attached once, so it reads the latest props through refs.
  const onCancelRef = useRef(onCancel);
  const isLoadingRef = useRef(isLoading);
  useEffect(() => {
    onCancelRef.current = onCancel;
    isLoadingRef.current = isLoading;
  });

  // Opened in a layout effect so its cleanup — close, then focus back to whatever opened it — runs before the
  // caller's own passive effects. A caller that moves focus somewhere better once the dialog has gone (the Sessions
  // page's confirmation banner) therefore still wins, as it did with MUI's Dialog.
  useLayoutEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return undefined;

    const opener = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    closingRef.current = false;

    let removeEscapeListener: (() => void) | undefined;
    if (isModal) {
      // showModal moves focus to the first focusable element, which is Cancel: the least destructive action.
      if (!dialog.open) dialog.showModal();
    } else {
      dialog.querySelector<HTMLElement>('button')?.focus();
      const handleKeyDown = (event: KeyboardEvent): void => {
        if (event.key === 'Escape' && !isLoadingRef.current) onCancelRef.current();
      };
      dialog.addEventListener('keydown', handleKeyDown);
      removeEscapeListener = () => dialog.removeEventListener('keydown', handleKeyDown);
    }

    return () => {
      closingRef.current = true;
      removeEscapeListener?.();
      if (isModal && dialog.open) dialog.close();
      if (opener?.isConnected) opener.focus();
    };
  }, [isModal]);

  // Escape. Kept open while the action is in flight, as the disabled Cancel button is.
  const handleCancel = (event: React.SyntheticEvent<HTMLDialogElement>): void => {
    event.preventDefault();
    if (!isLoading) onCancel();
  };

  // A close nobody here asked for: Chrome closes a modal on a second Escape even when the first was refused. The
  // open check skips the close that StrictMode's cleanup queues before it reopens the dialog.
  const handleClose = (): void => {
    if (closingRef.current || dialogRef.current?.open) return;
    onCancel();
  };

  return (
    <dialog
      ref={dialogRef}
      className={isModal ? 'delete-confirm-dialog' : 'delete-confirm-dialog delete-confirm-dialog--inline'}
      open={isModal ? undefined : true}
      aria-labelledby={titleId}
      aria-describedby={messageId}
      data-testid={testId}
      onCancel={handleCancel}
      onClose={handleClose}>
      <h2 id={titleId} className="govuk-heading-m delete-confirm-dialog__title" data-testid={`${testId}-title`}>
        {title}
      </h2>
      <div className="delete-confirm-dialog__content">
        <p className="govuk-body" id={messageId}>
          {message}
        </p>
        {itemName && (
          <p className="govuk-body delete-confirm-dialog__item-name" data-testid={`${testId}-item-name`}>
            {itemName}
          </p>
        )}
        {children}
        <div className="govuk-button-group">
          <GovUKButton className="govuk-button govuk-button--secondary" onClick={onCancel} disabled={isLoading} data-testid={`${testId}-cancel`}>
            Cancel
          </GovUKButton>
          <GovUKButton className="govuk-button govuk-button--warning" onClick={onConfirm} isLoading={isLoading} data-testid={`${testId}-confirm`}>
            {confirmLabel}
          </GovUKButton>
        </div>
      </div>
    </dialog>
  );
};

/**
 * A confirmation for a destructive action, on the native <dialog> element (BLUM-8758; it was MUI's Dialog). GOV.UK
 * has no modal component — its pattern is a confirmation page, which ConfirmActionPanel serves — so this is kept to
 * the in-place confirmations that were already dialogs.
 *
 * As a modal the browser supplies the focus trap, inert background, Escape and the top layer; focus returns to the
 * opener on close. Where showModal is missing (Safari before 15.4) it renders inline, non-modal, with the first
 * button focused and Escape handled here.
 */
const DeleteConfirmDialog = ({ open, ...props }: DeleteConfirmDialogProps): React.ReactElement | null => (open ? <OpenDialog {...props} /> : null);

export default DeleteConfirmDialog;
