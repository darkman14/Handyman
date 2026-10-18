import { fireEvent, render, screen } from '@testing-library/react-native';
import { Badge, Button, Card, Screen, Stars, Text, TextField } from '.';

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
