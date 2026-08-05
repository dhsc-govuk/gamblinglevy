import React from 'react';
import { Dialog, DialogTitle, DialogContent, DialogActions } from '@mui/material';
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

const DeleteConfirmDialog = ({
  open,
  title,
  message,
  itemName,
  onConfirm,
  onCancel,
  isLoading = false,
  testId = 'delete-confirm-dialog',
  confirmLabel = 'Remove',
  children,
}: DeleteConfirmDialogProps): React.ReactElement => {
  return (
    <Dialog
      open={open}
      onClose={onCancel}
      maxWidth="sm"
      fullWidth
      PaperProps={{ 'data-testid': testId } as React.HTMLAttributes<HTMLDivElement>}
    >
      <DialogTitle className="delete-confirm-dialog__title" data-testid={`${testId}-title`}>
        {title}
      </DialogTitle>
      <DialogContent className="delete-confirm-dialog__content">
        <p className="govuk-body">{message}</p>
        {itemName && (
          <p className="govuk-body delete-confirm-dialog__item-name" data-testid={`${testId}-item-name`}>
            {itemName}
          </p>
        )}
        {children}
      </DialogContent>
      <DialogActions className="delete-confirm-dialog__actions">
        <GovUKButton
          className="govuk-button govuk-button--secondary"
          onClick={onCancel}
          disabled={isLoading}
          data-testid={`${testId}-cancel`}
        >
          Cancel
        </GovUKButton>
        <GovUKButton
          className="govuk-button govuk-button--warning"
          onClick={onConfirm}
          isLoading={isLoading}
          data-testid={`${testId}-confirm`}
        >
          {confirmLabel}
        </GovUKButton>
      </DialogActions>
    </Dialog>
  );
};

export default DeleteConfirmDialog;
