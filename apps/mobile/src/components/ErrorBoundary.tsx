import { Ionicons } from '@expo/vector-icons';
import { Component, type PropsWithChildren, type ReactNode } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { Text } from './Text';
import { radius, spacing } from './theme';

type State = {
  hasError: boolean;
  error: Error | null;
};

export class ErrorBoundary extends Component<PropsWithChildren, State> {
  state: State = { hasError: false, error: null };

  static getDerivedStateFromError(error: Error): State {
    return { hasError: true, error };
  }

  componentDidCatch(error: Error, info: { componentStack?: string | null }) {
    console.error('[ErrorBoundary]', error, info.componentStack);
  }

  handleRetry = () => {
    this.setState({ hasError: false, error: null });
  };

  render(): ReactNode {
    if (this.state.hasError) {
      return (
        <View style={styles.container}>
          <View style={styles.card}>
            <View style={styles.iconWrap}>
              <Ionicons name="warning-outline" size={40} color="#DC2626" />
            </View>
            <Text variant="subtitle" style={styles.title}>Something went wrong</Text>
            <Text variant="body" style={styles.message}>
              {this.state.error?.message ?? 'An unexpected error occurred.'}
            </Text>
            <Pressable onPress={this.handleRetry} style={styles.button}>
              <Ionicons name="refresh-outline" size={20} color="#FFFFFF" />
              <Text variant="bodyBold" color="#FFFFFF">Try Again</Text>
            </Pressable>
          </View>
        </View>
      );
    }

    return this.props.children;
  }
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: '#0C0E14',
    alignItems: 'center',
    justifyContent: 'center',
    padding: spacing.xl,
  },
  card: {
    backgroundColor: '#171923',
    borderRadius: radius.xl,
    padding: spacing.xxl,
    alignItems: 'center',
    gap: spacing.lg,
    width: '100%',
    maxWidth: 360,
  },
  iconWrap: {
    width: 72,
    height: 72,
    borderRadius: 36,
    backgroundColor: '#3D0808',
    alignItems: 'center',
    justifyContent: 'center',
  },
  title: {
    color: '#ECEEF4',
    textAlign: 'center',
  },
  message: {
    color: '#8B93A8',
    textAlign: 'center',
    fontSize: 14,
  },
  button: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    backgroundColor: '#0B6B5B',
    paddingHorizontal: spacing.xl,
    paddingVertical: spacing.md,
    borderRadius: radius.md,
    marginTop: spacing.sm,
  },
});
