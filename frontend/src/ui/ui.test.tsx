import { fireEvent, render, screen } from '@testing-library/react-native';
import { Badge, Button, Card, ConfirmDialog, LoadingScreen, Screen, Stars, Text, TextField } from '.';

describe('Button', () => {
  it('renders, handles press and has an accessibility label', async () => {
    const onPress = jest.fn();
    await render(<Button title="Save" onPress={onPress} />);
    await fireEvent.press(screen.getByLabelText('Save'));
    expect(onPress).toHaveBeenCalledTimes(1);
  });
  it('does not fire when disabled', async () => {
    const onPress = jest.fn();
    await render(<Button title="Save" onPress={onPress} disabled />);
    await fireEvent.press(screen.getByLabelText('Save'));
    expect(onPress).not.toHaveBeenCalled();
  });
});

describe('Button loading', () => {
  it('is busy and does not fire while loading', async () => {
    const onPress = jest.fn();
    await render(<Button title="Send code" onPress={onPress} loading />);
    await fireEvent.press(screen.getByLabelText('Send code'));
    expect(onPress).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Send code' })).toBeBusy();
  });
});

describe('Text tone', () => {
  it('renders danger text as an alert', async () => {
    await render(<Text tone="danger" role="alert">Oops</Text>);
    expect(screen.getByRole('alert')).toBeTruthy();
  });
});

describe('Screen', () => {
  it('renders its children', async () => {
    await render(<Screen><Text>inside</Text></Screen>);
    expect(screen.getByText('inside')).toBeTruthy();
  });
});

describe('Card', () => {
  it('renders children and exposes an accessibility label', async () => {
    await render(<Card accessibilityLabel="Job card"><Text>x</Text></Card>);
    expect(screen.getByLabelText('Job card')).toBeTruthy();
  });
});

describe('TextField', () => {
  it('renders, handles input and has an accessibility label', async () => {
    const onChangeText = jest.fn();
    await render(<TextField label="Phone" onChangeText={onChangeText} error="Required" />);
    await fireEvent.changeText(screen.getByLabelText('Phone'), '123');
    expect(onChangeText).toHaveBeenCalledWith('123');
    expect(screen.getByText('Required')).toBeTruthy();
  });
  it('announces the error as an alert and marks the field invalid', async () => {
    await render(<TextField label="Phone" error="Required" />);
    expect(screen.getByRole('alert')).toHaveTextContent('Required');
    expect(screen.getByLabelText('Phone').props['aria-invalid']).toBe(true);
    expect(screen.getByLabelText('Phone').props['aria-describedby']).toBe(
      screen.getByRole('alert').props.nativeID,
    );
  });
  it('has no alert or invalid flag without an error', async () => {
    await render(<TextField label="Phone" />);
    expect(screen.queryByRole('alert')).toBeNull();
    expect(screen.getByLabelText('Phone').props['aria-invalid']).toBeUndefined();
  });
});

describe('Badge', () => {
  it('renders with an accessibility label', async () => {
    await render(<Badge label="Open" tone="success" />);
    expect(screen.getByLabelText('Open')).toBeTruthy();
  });
});

describe('Stars', () => {
  it('renders the value as an accessibility label', async () => {
    await render(<Stars value={3} />);
    expect(screen.getByLabelText('Rating 3 of 5')).toBeTruthy();
  });
  it('handles press', async () => {
    const onChange = jest.fn();
    await render(<Stars value={1} onChange={onChange} />);
    await fireEvent.press(screen.getByLabelText('4 stars'));
    expect(onChange).toHaveBeenCalledWith(4);
  });
});

describe('ConfirmDialog', () => {
  it('shows the title and fires confirm and cancel', async () => {
    const onConfirm = jest.fn();
    const onCancel = jest.fn();
    await render(
      <ConfirmDialog visible title="Sign out?" confirmLabel="Sign out" cancelLabel="Cancel" onConfirm={onConfirm} onCancel={onCancel} />,
    );
    expect(screen.getByText('Sign out?')).toBeTruthy();
    await fireEvent.press(screen.getByRole('button', { name: 'Cancel' }));
    expect(onCancel).toHaveBeenCalledTimes(1);
    await fireEvent.press(screen.getByRole('button', { name: 'Sign out' }));
    expect(onConfirm).toHaveBeenCalledTimes(1);
  });
  it('renders nothing when not visible', async () => {
    await render(
      <ConfirmDialog visible={false} title="Sign out?" confirmLabel="Sign out" cancelLabel="Cancel" onConfirm={jest.fn()} onCancel={jest.fn()} />,
    );
    expect(screen.queryByText('Sign out?')).toBeNull();
  });
});

describe('LoadingScreen', () => {
  it('shows its label', async () => {
    await render(<LoadingScreen label="Loading" />);
    expect(screen.getByText('Loading')).toBeTruthy();
  });
});
